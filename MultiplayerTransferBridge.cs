using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using UnityEngine;
using IDictionary = System.Collections.IDictionary;

namespace RealisticLiquidTransfer;

internal enum TransferCommitRoute
{
    Local,
    Host,
    Client,
    Unavailable
}

internal readonly struct LiquidAmount
{
    internal LiquidAmount(string liquidId, float amount)
    {
        LiquidId = liquidId;
        Amount = amount;
    }

    internal string LiquidId { get; }
    internal float Amount { get; }
}

internal sealed class ContainerLiquidChange
{
    internal ushort SyncId;
    internal byte Version;
    internal List<LiquidAmount> Expected;
    internal List<LiquidAmount> Target;
}

internal sealed class LiquidTransferCommitRequest
{
    internal Guid RequestId;
    internal bool DrainOnly;
    internal bool NoLiquidLoss;
    internal ContainerLiquidChange Receiver;
    internal ContainerLiquidChange Source;
}

internal static class MultiplayerTransferBridge
{
    private const string ChannelName = "realliquidtransfer.commit.v1";
    private const byte ProtocolVersion = 1;
    private const byte RequestMessage = 1;
    private const byte ResponseMessage = 2;
    private const int MaximumPayloadSize = 16384;
    private const int MaximumLiquidEntries = 64;
    private const float AmountTolerance = 0.02f;

    private static readonly HashSet<Guid> AppliedRequests = new HashSet<Guid>();
    private static readonly List<Guid> AppliedRequestOrder = new List<Guid>();

    private static Assembly multiplayerAssembly;
    private static Type multiplayerType;
    private static Type netType;
    private static MethodInfo createNamedWriter;
    private static MethodInfo registerServerReceiver;
    private static MethodInfo registerClientReceiver;
    private static MethodInfo clientSend;
    private static MethodInfo serverSendTo;
    private static MethodInfo writerPutBytes;
    private static MethodInfo readerGetBytes;
    private static MethodInfo itemReachCheck;
    private static Type deliveryMethodType;
    private static object reliableOrdered;
    private static bool initialized;
    private static Assembly failedAssembly;
    private static Assembly minigameSyncCompatibilityAttemptedAssembly;
    private static string lastDetectionState;
    private static string lastRouteState;

    internal static bool IsMultiplayerModLoaded => FindMultiplayerAssembly() != null;

    internal static void PatchMinigameSyncCompatibility(HarmonyLib.Harmony harmony)
    {
        var assembly = FindMultiplayerAssembly();
        if (assembly == null || assembly == minigameSyncCompatibilityAttemptedAssembly)
        {
            return;
        }

        minigameSyncCompatibilityAttemptedAssembly = assembly;
        try
        {
            var patchType = assembly.GetType(
                "Together.MinigameBase_StartMinigame_MultiplayerPatch",
                false);
            var postfix = patchType?.GetMethod(
                "Postfix",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (postfix == null
                || !postfix.GetParameters().Any(
                    parameter => parameter.Name == "minigame"
                        && parameter.ParameterType == typeof(Minigame)))
            {
                throw new MissingMethodException(
                    "Could not find Together's multiplayer minigame-start postfix.");
            }

            var prefix = typeof(MultiplayerTransferBridge).GetMethod(
                nameof(ShouldRunTogetherMinigamePatch),
                BindingFlags.NonPublic | BindingFlags.Static);
            harmony.Patch(postfix, prefix: new HarmonyLib.HarmonyMethod(prefix));
            DebugLog.Write(
                "Installed multiplayer compatibility patch to keep the RLT minigame local; "
                + "liquid results continue through the authoritative transfer channel.");
        }
        catch (Exception exception)
        {
            Plugin.ReportNetworkWarning(
                $"Could not suppress Together's minigame synchronization for RLT: {exception}");
        }
    }

    private static bool ShouldRunTogetherMinigamePatch(Minigame minigame)
    {
        if (minigame is LiquidTransferMinigame)
        {
            DebugLog.Write("Skipped Together minigame synchronization for LiquidTransferMinigame.");
            return false;
        }

        return true;
    }

    internal static void InitializeIfAvailable()
    {
        var assembly = FindMultiplayerAssembly();
        if (assembly == null)
        {
            LogDetectionState("Multiplayer detection: Together.Net assembly is not loaded.");
            return;
        }

        LogDetectionState(
            $"Multiplayer detection: found {assembly.FullName}; location='{GetAssemblyLocation(assembly)}'.");
        if (initialized || assembly == failedAssembly)
        {
            return;
        }

        try
        {
            multiplayerAssembly = assembly;
            multiplayerType = assembly.GetType("Together.Multiplayer", true);
            netType = assembly.GetType("Together.Net", true);

            createNamedWriter = FindStaticMethod(
                multiplayerType,
                "CreateNamedWriter",
                parameters => parameters.Length == 1 && parameters[0].ParameterType == typeof(string));
            registerServerReceiver = FindReceiverRegistration(multiplayerType, 2);
            registerClientReceiver = FindReceiverRegistration(multiplayerType, 1);
            deliveryMethodType = FindDeliveryMethodType(assembly);
            reliableOrdered = Enum.Parse(deliveryMethodType, "ReliableOrdered");

            if (createNamedWriter == null)
            {
                throw new MissingMethodException(
                    multiplayerType.FullName,
                    "CreateNamedWriter(string)");
            }

            var writerType = createNamedWriter.ReturnType;
            var netDataReaderType = FindReaderType(assembly);
            writerPutBytes = writerType.GetMethod(
                "PutBytesWithLength",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(byte[]) },
                null);
            readerGetBytes = netDataReaderType.GetMethod(
                "GetBytesWithLength",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            clientSend = FindSendMethod(netType, "Client_Send", writerType, deliveryMethodType, null);
            serverSendTo = FindSendMethod(
                netType,
                "Server_SendTo",
                writerType,
                deliveryMethodType,
                assembly.GetType("Together.ScavPlayer", true));
            itemReachCheck = FindItemReachCheck(assembly);

            DebugLog.Write(
                "Multiplayer API discovery: "
                + $"CreateNamedWriter={DescribeMethod(createNamedWriter)}; "
                + $"server receiver={DescribeMethod(registerServerReceiver)}; "
                + $"client receiver={DescribeMethod(registerClientReceiver)}; "
                + $"writer bytes={DescribeMethod(writerPutBytes)}; "
                + $"reader bytes={DescribeMethod(readerGetBytes)}; "
                + $"client send={DescribeMethod(clientSend)}; "
                + $"server send-to={DescribeMethod(serverSendTo)}; "
                + $"item reach check={DescribeMethod(itemReachCheck)}.");

            if (createNamedWriter == null
                || registerServerReceiver == null
                || registerClientReceiver == null
                || writerPutBytes == null
                || readerGetBytes == null
                || clientSend == null
                || serverSendTo == null)
            {
                throw new MissingMethodException("The multiplayer mod's named-message API is incomplete.");
            }

            var serverCallback = CreateReceiver(
                registerServerReceiver,
                typeof(MultiplayerTransferBridge).GetMethod(
                    nameof(HandleServerMessage),
                    BindingFlags.NonPublic | BindingFlags.Static));
            var clientCallback = CreateReceiver(
                registerClientReceiver,
                typeof(MultiplayerTransferBridge).GetMethod(
                    nameof(HandleClientMessage),
                    BindingFlags.NonPublic | BindingFlags.Static));

            registerServerReceiver.Invoke(null, new object[] { ChannelName, serverCallback });
            registerClientReceiver.Invoke(null, new object[] { ChannelName, clientCallback });
            initialized = true;
            DebugLog.Write(
                itemReachCheck == null
                    ? "Registered multiplayer liquid-transfer message handlers, but item reach checking is unavailable; host transfers will be rejected."
                    : "Registered multiplayer liquid-transfer message handlers; required reflection APIs were found.");
        }
        catch (Exception exception)
        {
            failedAssembly = assembly;
            initialized = false;
            Plugin.ReportNetworkError($"Could not initialize multiplayer liquid-transfer integration: {exception}");
        }
    }

    internal static TransferCommitRoute GetRoute()
    {
        var assembly = FindMultiplayerAssembly();
        if (assembly == null)
        {
            return LogRoute(TransferCommitRoute.Local, "multiplayer assembly not loaded");
        }

        InitializeIfAvailable();
        var type = assembly.GetType("Together.Net", false);
        if (type == null)
        {
            return LogRoute(TransferCommitRoute.Local, "Together.Net type not found");
        }

        try
        {
            if (!ReadBooleanProperty(type, "IsRunning"))
            {
                return LogRoute(TransferCommitRoute.Local, "multiplayer is not running");
            }

            if (!initialized)
            {
                return LogRoute(TransferCommitRoute.Unavailable, "multiplayer is running but integration is not initialized");
            }

            var isServer = ReadBooleanProperty(type, "IsServer");
            var isClient = ReadBooleanProperty(type, "IsClient");
            if (isServer)
            {
                return LogRoute(TransferCommitRoute.Host, $"running=true, server=true, client={isClient}");
            }

            if (isClient)
            {
                return LogRoute(TransferCommitRoute.Client, "running=true, server=false, client=true");
            }

            return LogRoute(TransferCommitRoute.Unavailable, "running=true, but neither server nor client");
        }
        catch (Exception exception)
        {
            Plugin.ReportNetworkError($"Could not determine multiplayer liquid-transfer route: {exception}");
            return LogRoute(TransferCommitRoute.Unavailable, $"role detection failed: {exception.Message}");
        }
    }

    internal static bool TryCaptureClientSyncIdentity(
        WaterContainerItem container,
        out ushort syncId,
        out byte version)
    {
        syncId = 0;
        version = 0;
        if (!container)
        {
            DebugLog.Write("Container detection failed: transfer container reference is missing.");
            return false;
        }

        if (!TryGetClientSyncInfo(container.gameObject, out var syncInfo))
        {
            return false;
        }

        if (!TryGetSyncIdentity(syncInfo, out syncId, out version))
        {
            return false;
        }

        DebugLog.Write(
            $"Captured multiplayer identity for '{container.gameObject.name}': syncId={syncId}, version={version}.");
        return true;
    }

    internal static bool TryCreateChange(
        WaterContainerItem container,
        ushort syncId,
        byte version,
        List<LiquidAmount> expected,
        List<LiquidAmount> target,
        out ContainerLiquidChange change)
    {
        change = null;
        if (syncId == 0 && !TryCaptureClientSyncIdentity(container, out syncId, out version))
        {
            return false;
        }

        if (expected == null || target == null)
        {
            DebugLog.Write("Container detection failed: transfer compositions are unavailable.");
            return false;
        }

        change = new ContainerLiquidChange
        {
            SyncId = syncId,
            Version = version,
            Expected = expected,
            Target = target
        };
        DebugLog.Write(
            $"Detected transfer container '{(container ? container.gameObject.name : "<reference unavailable>")}': "
            + $"syncId={syncId}, version={version}, "
            + $"current=[{FormatComposition(change.Expected)}], target=[{FormatComposition(change.Target)}].");
        return true;
    }

    internal static bool SendCommit(LiquidTransferCommitRequest request)
    {
        if (!initialized || request == null)
        {
            return false;
        }

        try
        {
            var payload = SerializeRequest(request);
            var writer = CreateNamedWriter(payload);
            clientSend.Invoke(null, new[] { reliableOrdered, writer });
            DebugLog.Write($"Sent multiplayer liquid-transfer commit {request.RequestId:N}.");
            return true;
        }
        catch (Exception exception)
        {
            Plugin.ReportNetworkError($"Could not send multiplayer liquid-transfer commit: {exception}");
            return false;
        }
    }

    private static Assembly FindMultiplayerAssembly()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetType("Together.Net", false) != null)
            {
                return assembly;
            }
        }

        return null;
    }

    private static string GetAssemblyLocation(Assembly assembly)
    {
        try
        {
            return string.IsNullOrEmpty(assembly.Location) ? "<dynamic>" : assembly.Location;
        }
        catch (NotSupportedException)
        {
            return "<unavailable>";
        }
    }

    private static string DescribeMethod(MethodInfo method)
    {
        return method == null ? "<missing>" : method.ToString();
    }

    [System.Diagnostics.Conditional("LIQUID_TRANSFER_DEBUG_LOGGING")]
    private static void LogDetectionState(string state)
    {
        if (string.Equals(lastDetectionState, state, StringComparison.Ordinal))
        {
            return;
        }

        lastDetectionState = state;
        DebugLog.Write(state);
    }

    private static TransferCommitRoute LogRoute(TransferCommitRoute route, string details)
    {
        var state = $"Multiplayer transfer route: {route} ({details}).";
        if (!string.Equals(lastRouteState, state, StringComparison.Ordinal))
        {
            lastRouteState = state;
            DebugLog.Write(state);
        }

        return route;
    }

    private static string FormatComposition(List<LiquidAmount> amounts)
    {
        return amounts == null
            ? "<missing>"
            : string.Join(", ", amounts.Select(entry => $"{entry.LiquidId}={entry.Amount:F2} ml"));
    }

    private static MethodInfo FindStaticMethod(
        Type type,
        string name,
        Predicate<ParameterInfo[]> parameterMatch)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (method.Name == name && parameterMatch(method.GetParameters()))
            {
                return method;
            }
        }

        return null;
    }

    private static MethodInfo FindReceiverRegistration(Type type, int callbackParameterCount)
    {
        return FindStaticMethod(
            type,
            callbackParameterCount == 2
                ? "RegisterCustomServerReceiver"
                : "RegisterCustomClientReceiver",
            parameters => parameters.Length == 2
                && parameters[0].ParameterType == typeof(string)
                && typeof(Delegate).IsAssignableFrom(parameters[1].ParameterType)
                && parameters[1].ParameterType.GetMethod("Invoke")?.GetParameters().Length
                    == callbackParameterCount);
    }

    private static Type FindDeliveryMethodType(Assembly assembly)
    {
        var liteNetLib = FindLiteNetLibAssembly(assembly);
        return liteNetLib?.GetType("LiteNetLib.DeliveryMethod", false)
            ?? assembly.GetType("LiteNetLib.DeliveryMethod", false);
    }

    private static Type FindReaderType(Assembly assembly)
    {
        var liteNetLib = FindLiteNetLibAssembly(assembly);
        return liteNetLib?.GetType("LiteNetLib.Utils.NetDataReader", false)
            ?? assembly.GetType("LiteNetLib.Utils.NetDataReader", false);
    }

    private static Assembly FindLiteNetLibAssembly(Assembly assembly)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(
            candidate => candidate.GetName().Name == "LiteNetLib");
        if (loaded != null)
        {
            return loaded;
        }

        var path = Path.Combine(Path.GetDirectoryName(assembly.Location), "LiteNetLib.dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    private static MethodInfo FindSendMethod(
        Type type,
        string name,
        Type writerType,
        Type deliveryType,
        Type targetType)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (method.Name != name)
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != (targetType == null ? 2 : 3)
                || Unwrap(parameters[0].ParameterType) != deliveryType
                || Unwrap(parameters[1].ParameterType) != writerType
                || (targetType != null && Unwrap(parameters[2].ParameterType) != targetType))
            {
                continue;
            }

            return method;
        }

        return null;
    }

    private static Type Unwrap(Type type)
    {
        return type.IsByRef ? type.GetElementType() : type;
    }

    private static Delegate CreateReceiver(MethodInfo registration, MethodInfo handler)
    {
        var delegateType = registration.GetParameters()[1].ParameterType;
        var callbackParameters = delegateType.GetMethod("Invoke").GetParameters();
        var parameters = new ParameterExpression[callbackParameters.Length];
        var boxedArguments = new Expression[callbackParameters.Length];
        for (var index = 0; index < callbackParameters.Length; index++)
        {
            parameters[index] = Expression.Parameter(callbackParameters[index].ParameterType);
            boxedArguments[index] = Expression.Convert(parameters[index], typeof(object));
        }

        var call = Expression.Call(
            handler,
            Expression.NewArrayInit(typeof(object), boxedArguments));
        return Expression.Lambda(delegateType, call, parameters).Compile();
    }

    private static MethodInfo FindItemReachCheck(Assembly assembly)
    {
        var itemSync = assembly.GetType("Together.ItemSync", false);
        if (itemSync == null)
        {
            return null;
        }

        foreach (var method in itemSync.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            var parameters = method.GetParameters();
            if (method.Name == "CheckIfBodyReachThisItem"
                && parameters.Length == 4
                && parameters[0].ParameterType.Name == "SyncInfo"
                && parameters[1].ParameterType == typeof(Body)
                && parameters[2].ParameterType == typeof(float)
                && parameters[3].ParameterType == typeof(bool))
            {
                return method;
            }
        }

        return null;
    }

    private static bool ReadBooleanProperty(Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
        if (property == null || property.PropertyType != typeof(bool))
        {
            throw new MissingMemberException(type.FullName, name);
        }

        return (bool)property.GetValue(null, null);
    }

    private static bool TryGetClientSyncInfo(GameObject gameObject, out object syncInfo)
    {
        syncInfo = null;
        var registryType = multiplayerAssembly?.GetType("Together.NetObjectRegistry", false);
        var registry = registryType?.GetField(
            "SyncRegistry",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IDictionary;
        if (registry == null || !registry.Contains(gameObject))
        {
            DebugLog.Write(
                registry == null
                    ? "Container detection failed: multiplayer SyncRegistry is unavailable."
                    : $"Container detection failed: '{gameObject.name}' is not in multiplayer SyncRegistry.");
            return false;
        }

        syncInfo = registry[gameObject];
        if (syncInfo == null)
        {
            DebugLog.Write($"Container detection failed: '{gameObject.name}' has a null multiplayer sync entry.");
        }

        return syncInfo != null;
    }

    private static bool TryGetSyncIdentity(object syncInfo, out ushort syncId, out byte version)
    {
        syncId = 0;
        version = 0;
        if (syncInfo == null)
        {
            DebugLog.Write("Container detection failed: multiplayer sync information is null.");
            return false;
        }

        if (ReadMember(syncInfo, "IsDestroyed") is bool destroyed && destroyed)
        {
            DebugLog.Write("Container detection failed: multiplayer sync information is destroyed.");
            return false;
        }

        var rawSyncId = ReadMember(syncInfo, "syncId");
        var rawId = ReadMember(rawSyncId, "id");
        if (rawId == null)
        {
            DebugLog.Write("Container detection failed: multiplayer sync ID is unavailable.");
            return false;
        }

        syncId = Convert.ToUInt16(rawId);
        version = Convert.ToByte(ReadMember(syncInfo, "Version"));
        if (syncId == 0)
        {
            DebugLog.Write("Container detection failed: multiplayer sync ID is zero.");
            return false;
        }

        return true;
    }

    private static object ReadMember(object instance, string name)
    {
        if (instance == null)
        {
            return null;
        }

        var type = instance.GetType();
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            return field.GetValue(instance);
        }

        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return property?.GetValue(instance, null);
    }

    private static object FindServerSyncInfo(ushort syncId)
    {
        var registryType = multiplayerAssembly?.GetType("Together.NetObjectRegistry", false);
        var registry = registryType?.GetField(
            "NetIdToSyncInfoDict",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IDictionary;
        if (registry == null)
        {
            return null;
        }

        var keyType = registry.GetType().GetGenericArguments()[0];
        var key = Activator.CreateInstance(keyType, new object[] { syncId });
        return registry.Contains(key) ? registry[key] : null;
    }

    private static List<LiquidAmount> CaptureComposition(WaterContainerItem container)
    {
        var amounts = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var stack in container.stack)
        {
            if (stack == null || string.IsNullOrWhiteSpace(stack.liquidId) || stack.amount <= 0f)
            {
                continue;
            }

            amounts.TryGetValue(stack.liquidId, out var current);
            amounts[stack.liquidId] = current + stack.amount;
        }

        var result = new List<LiquidAmount>(amounts.Count);
        foreach (var entry in amounts)
        {
            result.Add(new LiquidAmount(entry.Key, entry.Value));
        }

        return result;
    }

    private static byte[] SerializeRequest(LiquidTransferCommitRequest request)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(ProtocolVersion);
            writer.Write(RequestMessage);
            writer.Write(request.RequestId.ToByteArray());
            writer.Write(request.DrainOnly);
            writer.Write(request.NoLiquidLoss);
            WriteChange(writer, request.Receiver);
            if (!request.DrainOnly)
            {
                WriteChange(writer, request.Source);
            }

            writer.Flush();
            if (stream.Length > MaximumPayloadSize)
            {
                throw new InvalidDataException("Liquid-transfer request exceeded the payload limit.");
            }

            return stream.ToArray();
        }
    }

    private static void WriteChange(BinaryWriter writer, ContainerLiquidChange change)
    {
        writer.Write(change.SyncId);
        writer.Write(change.Version);
        WriteAmounts(writer, change.Expected);
        WriteAmounts(writer, change.Target);
    }

    private static string FormatChange(ContainerLiquidChange change)
    {
        return change == null
            ? "<missing>"
            : $"syncId={change.SyncId}/v{change.Version}, current=[{FormatComposition(change.Expected)}], "
                + $"target=[{FormatComposition(change.Target)}]";
    }

    private static void WriteAmounts(BinaryWriter writer, List<LiquidAmount> amounts)
    {
        if (amounts == null || amounts.Count > MaximumLiquidEntries)
        {
            throw new InvalidDataException("Invalid liquid-entry count.");
        }

        writer.Write((byte)amounts.Count);
        foreach (var amount in amounts)
        {
            writer.Write(amount.LiquidId ?? string.Empty);
            writer.Write(amount.Amount);
        }
    }

    private static void HandleServerMessage(object[] arguments)
    {
        var sender = arguments != null && arguments.Length > 0 ? arguments[0] : null;
        var reader = arguments != null && arguments.Length > 1 ? arguments[1] : null;
        var requestId = Guid.Empty;
        var accepted = false;
        var reason = "Invalid liquid-transfer request.";

        try
        {
            if (sender == null || reader == null)
            {
                throw new InvalidDataException("Missing multiplayer sender or packet.");
            }

            var payload = ReadPayload(reader);
            var request = DeserializeRequest(payload);
            requestId = request.RequestId;
            DebugLog.Write(
                $"Host received liquid-transfer request {requestId:N} from {sender.GetType().Name}: "
                + $"drainOnly={request.DrainOnly}, noLiquidLoss={request.NoLiquidLoss}, "
                + $"receiver={FormatChange(request.Receiver)}, "
                + $"source={(request.Source == null ? "<none>" : FormatChange(request.Source))}.");

            if (AppliedRequests.Contains(requestId))
            {
                accepted = true;
                reason = "Already applied.";
            }
            else if (TryApplyRequest(sender, request, out reason))
            {
                accepted = true;
                RememberRequest(requestId);
            }
        }
        catch (Exception exception)
        {
            reason = exception.Message;
            Plugin.ReportNetworkError($"Rejected multiplayer liquid-transfer request: {exception}");
        }

        DebugLog.Write(
            accepted
                ? $"Host accepted liquid-transfer request {requestId:N}."
                : $"Host rejected liquid-transfer request {requestId:N}: {reason}");
        SendResponse(sender, requestId, accepted, reason);
    }

    private static void HandleClientMessage(object[] arguments)
    {
        try
        {
            if (arguments == null || arguments.Length == 0 || arguments[0] == null)
            {
                throw new InvalidDataException("Missing multiplayer response packet.");
            }

            var payload = ReadPayload(arguments[0]);
            using (var stream = new MemoryStream(payload, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadByte() != ProtocolVersion || reader.ReadByte() != ResponseMessage)
                {
                    throw new InvalidDataException("Unsupported multiplayer response.");
                }

                var requestIdBytes = reader.ReadBytes(16);
                if (requestIdBytes.Length != 16)
                {
                    throw new EndOfStreamException();
                }

                var requestId = new Guid(requestIdBytes);
                var accepted = reader.ReadBoolean();
                var reason = reader.ReadString();
                if (accepted)
                {
                    DebugLog.Write($"Host accepted liquid-transfer commit {requestId:N}.");
                }
                else
                {
                    Plugin.ReportNetworkWarning(
                        $"Host rejected liquid-transfer commit {requestId:N}: {reason}");
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.ReportNetworkError($"Could not read multiplayer liquid-transfer response: {exception}");
        }
    }

    private static LiquidTransferCommitRequest DeserializeRequest(byte[] payload)
    {
        if (payload == null || payload.Length > MaximumPayloadSize)
        {
            throw new InvalidDataException("Invalid liquid-transfer payload size.");
        }

        using (var stream = new MemoryStream(payload, false))
        using (var reader = new BinaryReader(stream, Encoding.UTF8))
        {
            if (reader.ReadByte() != ProtocolVersion || reader.ReadByte() != RequestMessage)
            {
                throw new InvalidDataException("Unsupported liquid-transfer request.");
            }

            var requestIdBytes = reader.ReadBytes(16);
            if (requestIdBytes.Length != 16)
            {
                throw new EndOfStreamException();
            }

            var request = new LiquidTransferCommitRequest
            {
                RequestId = new Guid(requestIdBytes),
                DrainOnly = reader.ReadBoolean(),
                NoLiquidLoss = reader.ReadBoolean(),
                Receiver = ReadChange(reader)
            };
            if (!request.DrainOnly)
            {
                request.Source = ReadChange(reader);
            }

            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException("Unexpected trailing liquid-transfer data.");
            }

            return request;
        }
    }

    private static ContainerLiquidChange ReadChange(BinaryReader reader)
    {
        return new ContainerLiquidChange
        {
            SyncId = reader.ReadUInt16(),
            Version = reader.ReadByte(),
            Expected = ReadAmounts(reader),
            Target = ReadAmounts(reader)
        };
    }

    private static List<LiquidAmount> ReadAmounts(BinaryReader reader)
    {
        var count = reader.ReadByte();
        if (count > MaximumLiquidEntries)
        {
            throw new InvalidDataException("Too many liquid types in transfer request.");
        }

        var result = new List<LiquidAmount>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(new LiquidAmount(reader.ReadString(), reader.ReadSingle()));
        }

        return result;
    }

    private static byte[] ReadPayload(object reader)
    {
        var payload = readerGetBytes.Invoke(reader, null) as byte[];
        if (payload == null || payload.Length > MaximumPayloadSize)
        {
            throw new InvalidDataException("Invalid liquid-transfer payload.");
        }

        return payload;
    }

    private static bool TryApplyRequest(
        object sender,
        LiquidTransferCommitRequest request,
        out string reason)
    {
        reason = "Transfer validation failed.";
        if (request.RequestId == Guid.Empty
            || request.Receiver == null
            || request.Receiver.SyncId == 0
            || request.DrainOnly == (request.Source != null)
            || (request.Source != null && request.Source.SyncId == request.Receiver.SyncId))
        {
            return false;
        }

        var body = ReadMember(sender, "body") as Body;
        if (!body || !body.conscious)
        {
            reason = "The requesting player is unavailable or unconscious.";
            return false;
        }

        var receiverSyncInfo = FindServerSyncInfo(request.Receiver.SyncId);
        var sourceSyncInfo = request.Source != null
            ? FindServerSyncInfo(request.Source.SyncId)
            : null;
        if (!TryResolveContainer(receiverSyncInfo, request.Receiver, out var receiver))
        {
            reason = "A transfer container is missing, stale, or changed generation.";
            return false;
        }

        WaterContainerItem source = null;
        if (request.Source != null)
        {
            if (!TryResolveContainer(sourceSyncInfo, request.Source, out source))
            {
                reason = "A transfer container is missing, stale, or changed generation.";
                return false;
            }

            if (!IsReachable(sourceSyncInfo, body) || !IsReachable(receiverSyncInfo, body))
            {
                reason = "A transfer container is out of reach.";
                return false;
            }

            var receiverItem = receiver.GetComponent<Item>();
            var sourceItem = source.GetComponent<Item>();
            if (!receiverItem || !sourceItem || !body.CanCombine(receiverItem, sourceItem))
            {
                reason = "The containers can no longer be combined.";
                return false;
            }
        }
        else if (!IsReachable(receiverSyncInfo, body))
        {
            reason = "The drain container is out of reach.";
            return false;
        }

        var allowedLiquidIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in request.Receiver.Expected)
        {
            allowedLiquidIds.Add(entry.LiquidId);
        }

        if (request.Source != null)
        {
            foreach (var entry in request.Source.Expected)
            {
                allowedLiquidIds.Add(entry.LiquidId);
            }
        }

        if (!ValidateChange(request.Receiver, receiver, allowedLiquidIds, out reason)
            || (request.Source != null
                && !ValidateChange(request.Source, source, allowedLiquidIds, out reason)))
        {
            return false;
        }

        var initialTotal = Total(request.Receiver.Expected)
            + (request.Source != null ? Total(request.Source.Expected) : 0f);
        var targetTotal = Total(request.Receiver.Target)
            + (request.Source != null ? Total(request.Source.Target) : 0f);
        if (targetTotal > initialTotal + AmountTolerance)
        {
            reason = "The transfer result creates liquid.";
            return false;
        }

        var originalReceiver = CaptureComposition(receiver);
        var originalSource = source ? CaptureComposition(source) : null;
        try
        {
            ReplaceComposition(receiver, request.Receiver.Target);
            if (source)
            {
                ReplaceComposition(source, request.Source.Target);
            }
        }
        catch
        {
            ReplaceComposition(receiver, originalReceiver);
            if (source)
            {
                ReplaceComposition(source, originalSource);
            }

            throw;
        }

        PublishContainer(receiverSyncInfo);
        if (sourceSyncInfo != null)
        {
            PublishContainer(sourceSyncInfo);
        }

        reason = null;
        DebugLog.Write(
            $"Applied multiplayer liquid-transfer commit {request.RequestId:N}; "
            + $"receiver={receiver.CurrentTotal:F2} ml, "
            + $"source={(source ? source.CurrentTotal : 0f):F2} ml, "
            + $"no liquid loss={request.NoLiquidLoss}.");
        return true;
    }

    private static bool TryResolveContainer(
        object syncInfo,
        ContainerLiquidChange change,
        out WaterContainerItem container)
    {
        container = null;
        if (syncInfo == null
            || ReadMember(syncInfo, "IsDestroyed") is bool destroyed && destroyed
            || !TryGetSyncIdentity(syncInfo, out var syncId, out var version)
            || syncId != change.SyncId
            || version != change.Version)
        {
            return false;
        }

        var gameObject = ReadMember(syncInfo, "go") as GameObject;
        if (!gameObject)
        {
            return false;
        }

        container = gameObject.GetComponent<WaterContainerItem>();
        return container && container.stack != null;
    }

    private static bool ValidateChange(
        ContainerLiquidChange change,
        WaterContainerItem container,
        HashSet<string> allowedLiquidIds,
        out string reason)
    {
        reason = "Invalid container liquid state.";
        if (!ValidateAmounts(change.Expected) || !ValidateAmounts(change.Target))
        {
            return false;
        }

        if (!CompositionMatches(change.Expected, CaptureComposition(container)))
        {
            reason = "The container changed while the minigame was open.";
            return false;
        }

        var targetTotal = Total(change.Target);
        if (float.IsNaN(container.Capacity)
            || float.IsInfinity(container.Capacity)
            || container.Capacity < 0f
            || targetTotal > container.Capacity + AmountTolerance)
        {
            reason = "The transfer result exceeds container capacity.";
            return false;
        }

        foreach (var entry in change.Target)
        {
            if (!allowedLiquidIds.Contains(entry.LiquidId))
            {
                reason = $"The transfer result introduces unknown source liquid '{entry.LiquidId}'.";
                return false;
            }
        }

        reason = null;
        return true;
    }

    private static bool ValidateAmounts(List<LiquidAmount> amounts)
    {
        if (amounts == null || amounts.Count > MaximumLiquidEntries || Liquids.Registry == null)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in amounts)
        {
            if (string.IsNullOrWhiteSpace(entry.LiquidId)
                || !seen.Add(entry.LiquidId)
                || !Liquids.Registry.ContainsKey(entry.LiquidId)
                || float.IsNaN(entry.Amount)
                || float.IsInfinity(entry.Amount)
                || entry.Amount < 0f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool CompositionMatches(
        List<LiquidAmount> expected,
        List<LiquidAmount> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        var actualById = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var entry in actual)
        {
            actualById[entry.LiquidId] = entry.Amount;
        }

        foreach (var entry in expected)
        {
            if (!actualById.TryGetValue(entry.LiquidId, out var amount)
                || Mathf.Abs(amount - entry.Amount) > AmountTolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static float Total(List<LiquidAmount> amounts)
    {
        var total = 0f;
        foreach (var entry in amounts)
        {
            total += entry.Amount;
        }

        return total;
    }

    private static void ReplaceComposition(
        WaterContainerItem container,
        List<LiquidAmount> amounts)
    {
        container.stack.Clear();
        foreach (var entry in amounts)
        {
            if (entry.Amount > 0f)
            {
                container.stack.Add(new LiquidStack(entry.LiquidId, entry.Amount));
            }
        }

        container.UpdateCondition();
    }

    private static bool IsReachable(object syncInfo, Body body)
    {
        if (itemReachCheck == null)
        {
            return false;
        }

        return (bool)itemReachCheck.Invoke(
            null,
            new object[] { syncInfo, body, 20f, false });
    }

    private static void PublishContainer(object syncInfo)
    {
        try
        {
            var method = syncInfo.GetType().GetMethod(
                "WriteObjectIntoPacket",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                Plugin.ReportNetworkWarning(
                    $"Could not publish liquid-transfer item sync: {syncInfo.GetType().FullName} has no WriteObjectIntoPacket method.");
                return;
            }

            method.Invoke(syncInfo, null);
            DebugLog.Write($"Published multiplayer item sync through {DescribeMethod(method)}.");
        }
        catch (Exception exception)
        {
            Plugin.ReportNetworkWarning(
                $"Liquid transfer was applied but immediate item sync failed; normal sync will retry: {exception}");
        }
    }

    private static void SendResponse(object sender, Guid requestId, bool accepted, string reason)
    {
        if (sender == null || requestId == Guid.Empty)
        {
            return;
        }

        try
        {
            byte[] payload;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(ProtocolVersion);
                writer.Write(ResponseMessage);
                writer.Write(requestId.ToByteArray());
                writer.Write(accepted);
                writer.Write(reason ?? string.Empty);
                writer.Flush();
                payload = stream.ToArray();
            }

            var writerObject = CreateNamedWriter(payload);
            serverSendTo.Invoke(null, new[] { reliableOrdered, writerObject, sender });
            DebugLog.Write($"Sent liquid-transfer response {requestId:N}: accepted={accepted}, reason='{reason}'.");
        }
        catch (Exception exception)
        {
            Plugin.ReportNetworkError($"Could not acknowledge multiplayer liquid transfer: {exception}");
        }
    }

    private static object CreateNamedWriter(byte[] payload)
    {
        var writer = createNamedWriter.Invoke(null, new object[] { ChannelName });
        writerPutBytes.Invoke(writer, new object[] { payload });
        return writer;
    }

    private static void RememberRequest(Guid requestId)
    {
        AppliedRequests.Add(requestId);
        AppliedRequestOrder.Add(requestId);
        while (AppliedRequestOrder.Count > 256)
        {
            AppliedRequests.Remove(AppliedRequestOrder[0]);
            AppliedRequestOrder.RemoveAt(0);
        }
    }
}

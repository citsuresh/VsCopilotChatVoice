using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;

namespace VsCopilotChatVoice.VisualStudio;

// Diagnostic-only capability probe. No input, submission or skill activation is exposed.
internal sealed class ChatBridgePoc : IDisposable
{
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private readonly object sync = new object();
    private readonly Action<Exception> reportError;
    private readonly string pipeName;
    private readonly TimeSpan connectionTimeout;
    private readonly Func<CancellationToken, Task<ProbeSnapshot>> capture;
    private NamedPipeServerStream? activePipe;
    private ChatTargetObservation? observation;
    private readonly Task worker;

    internal static bool IsEnabled()
    {
#if DEBUG
        string[] args = Environment.GetCommandLineArgs();
        return Environment.GetEnvironmentVariable("VSCOPILOTCHATVOICE_BRIDGE_POC") == "1" &&
            Enumerable.Range(0, Math.Max(0, args.Length - 1)).Any(index =>
                string.Equals(args[index], "/RootSuffix", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(args[index + 1], "Exp", StringComparison.OrdinalIgnoreCase));
#else
        return false;
#endif
    }

    internal ChatBridgePoc(Action<Exception> reportError)
        : this(reportError, "VsCopilotChatVoice.Poc." + Process.GetCurrentProcess().Id,
            TimeSpan.FromSeconds(15), null, true)
    {
    }

    internal ChatBridgePoc(Action<Exception> reportError, string pipeName, TimeSpan connectionTimeout,
        Func<CancellationToken, Task<ProbeSnapshot>> capture)
        : this(reportError, pipeName, connectionTimeout, capture, false)
    {
    }

    private ChatBridgePoc(Action<Exception> reportError, string pipeName, TimeSpan connectionTimeout,
        Func<CancellationToken, Task<ProbeSnapshot>>? capture, bool nativeCapture)
    {
        this.reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        this.pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        if (connectionTimeout <= TimeSpan.Zero || connectionTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(connectionTimeout));
        this.connectionTimeout = connectionTimeout;
        this.capture = nativeCapture ? CaptureAsync : capture ?? throw new ArgumentNullException(nameof(capture));
        worker = Task.Run(RunAsync);
    }

    private async Task<ProbeSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        return CaptureOnUiThread(cancellationToken);
    }

    private ProbeSnapshot CaptureOnUiThread(CancellationToken cancellationToken)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        lock (sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lifetime.Token.ThrowIfCancellationRequested();
            ProbeSnapshot snapshot = Inspect(Application.Current.Windows.Cast<Window>());
            List<FrameworkElement> targets = snapshot.NativeTargets;
            snapshot.TargetStatus = targets.Count == 0 ? "NoNativeTarget" : "AmbiguousNativeTarget";
            if (targets.Count != 1)
            {
                observation?.Dispose();
                observation = null;
                return snapshot;
            }
            FrameworkElement anchor = targets[0];
            object context = anchor.DataContext;
            object session = ReadObservedBinding(context, "Session");
            object composer = ReadObservedBinding(context, "Composer");
            object input = ReadObservedBinding(composer, "PromptInput");
            if (session.GetType().FullName != "Microsoft.VisualStudio.Conversations.Backends.CopilotSdk.CopilotSdkSession" ||
                input.GetType().FullName != "Microsoft.VisualStudio.Copilot.UI.HarnessUi.Sessions.Composer.PromptTextInputViewModel" ||
                !(context is INotifyPropertyChanged contextChanges) || !(input is INotifyPropertyChanged inputChanges) ||
                !(ReadObservedBinding(session, "IsTrusted") is IObservable<bool> trusted) ||
                !(ReadObservedBinding(session, "IsResponding") is IObservable<bool> responding))
                throw new InvalidDataException("The native target observation contract is unsupported.");
            bool isReadOnly = ReadObservedBinding(session, "IsReadOnly") is bool readOnly ? readOnly :
                throw new InvalidDataException("The native read-only contract is unsupported.");
            bool isTranscript = ReadObservedBinding(session, "IsTranscript") is bool transcript ? transcript :
                throw new InvalidDataException("The native transcript contract is unsupported.");
            if (observation == null || !observation.Matches(anchor, context, session, composer, input))
            {
                observation?.Dispose();
                observation = null;
                observation = new ChatTargetObservation(anchor, contextChanges, session, composer, inputChanges,
                    trusted, responding, reportError, ScheduleOnUiThreadAsync);
            }
            snapshot.Target = observation.Snapshot();
            snapshot.TargetStatus = "Observed";
            snapshot.IsReadOnly = isReadOnly;
            snapshot.IsTranscript = isTranscript;
            return snapshot;
        }
    }

    private static async Task ScheduleOnUiThreadAsync(Action action)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        action();
    }

    private async Task RunAsync()
    {
        try
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            SecurityIdentifier user = WindowsIdentity.GetCurrent().User ??
                throw new InvalidOperationException("The current user SID is unavailable.");
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
            while (!lifetime.IsCancellationRequested)
            {
                using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 262144, security))
                using (var connection = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    lock (sync)
                    {
                        if (lifetime.IsCancellationRequested) return;
                        activePipe = pipe;
                    }
                    try
                    {
                        await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                        connection.CancelAfter(connectionTimeout);
                        // Framework pipe reads may ignore token cancellation; close the actual connection.
                        using (connection.Token.Register(pipe.Dispose))
                        {
                            byte[] request = new byte[8];
                            int count = 0;
                            bool terminated = false;
                            while (count < request.Length)
                            {
                                int read = await pipe.ReadAsync(request, count, 1, connection.Token).ConfigureAwait(false);
                                connection.Token.ThrowIfCancellationRequested();
                                if (read == 0) break;
                                if (request[count] == '\n') { terminated = true; break; }
                                count += read;
                            }
                            if (!terminated || Encoding.ASCII.GetString(request, 0, count) != "inspect")
                                throw new InvalidDataException("The diagnostic bridge accepts only inspect.");

                            ProbeSnapshot snapshot = await capture(connection.Token).ConfigureAwait(false);
                            connection.Token.ThrowIfCancellationRequested();
                            using (var output = new MemoryStream())
                            {
                                new DataContractJsonSerializer(typeof(ProbeSnapshot)).WriteObject(output, snapshot);
                                byte[] response = output.ToArray();
                                if (response.Length > 262144) throw new InvalidDataException("Probe response exceeds its limit.");
                                await pipe.WriteAsync(response, 0, response.Length, connection.Token).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception error) when (connection.IsCancellationRequested &&
                        (error is OperationCanceledException || error is IOException || error is ObjectDisposedException))
                    {
                        if (!lifetime.IsCancellationRequested)
                            reportError(new TimeoutException("The diagnostic connection timed out.", error));
                    }
                    catch (InvalidDataException error) { reportError(error); }
                    catch (IOException error) { reportError(error); }
                    catch (InvalidOperationException error) { reportError(error); }
                    catch (TargetInvocationException error) { reportError(error); }
                    catch (AmbiguousMatchException error) { reportError(error); }
                    catch (SerializationException error) { reportError(error); }
                    finally { lock (sync) { activePipe = null; } }
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
            error is InvalidOperationException || error is System.Security.SecurityException)
        {
            reportError(error);
        }
    }

    internal static ProbeSnapshot Inspect(IEnumerable<Window> windows)
    {
        var result = new ProbeSnapshot();
        var pending = new Stack<DependencyObject>();
        foreach (Window window in windows)
        {
            window.Dispatcher.VerifyAccess();
            if (window.IsVisible) pending.Push(window);
        }
        int visited = 0;
        while (pending.Count != 0)
        {
            if (++visited > 12000) throw new InvalidOperationException("Chat probe discovery exceeded its node limit.");
            DependencyObject node = pending.Pop();
            if (node is FrameworkElement element && element.IsVisible)
            {
                string id = AutomationProperties.GetAutomationId(element);
                string name = element.GetType().Name;
                if (id == "session" || id == "title" || name == "ChatSessionList" ||
                    name == "HarnessToolPicker" || id == "WpfTextViewHost")
                {
                    if (result.Controls.Count == 32) throw new InvalidOperationException("Too many probe controls.");
                    result.Controls.Add(Describe(element, id));
                    if (element.GetType().FullName == "Microsoft.VisualStudio.Conversations.UI.Internal.ChatWindow.Controls.ChatSessionList" &&
                        element.DataContext?.GetType().FullName == "Microsoft.VisualStudio.Copilot.UI.HarnessUi.HarnessSessionViewModel")
                        result.NativeTargets.Add(element);
                }
            }
            for (int child = VisualTreeHelper.GetChildrenCount(node) - 1; child >= 0; child--)
                pending.Push(VisualTreeHelper.GetChild(node, child));
        }
        result.Visited = visited;
        return result;
    }

    private static ProbeControl Describe(FrameworkElement element, string id)
    {
        object? context = element.DataContext;
        var control = new ProbeControl
        {
            Type = element.GetType().FullName ?? element.GetType().Name,
            AutomationId = id,
            ContextType = context?.GetType().FullName ?? "(none)"
        };
        if (context == null) return control;
        ProbeBinding root = DescribeBinding(context, "", context.GetType());
        control.Properties = root.Properties;
        control.Identities = root.Identities;
        foreach (string name in new[] { "Session", "Composer", "ChatLog" })
        {
            PropertyInfo? property = FindBindingProperty(context.GetType(), name);
            if (property == null || property.GetMethod == null || !property.GetMethod.IsPublic) continue;
            object? related = property.GetValue(context);
            control.Bindings.Add(DescribeBinding(related, name, property.PropertyType));
            if (name != "Composer" || related == null) continue;
            foreach (string childName in new[] { "PromptInput", "ActionButton" })
            {
                PropertyInfo? child = FindBindingProperty(related.GetType(), childName);
                if (child == null || child.GetMethod == null || !child.GetMethod.IsPublic) continue;
                control.Bindings.Add(DescribeBinding(child.GetValue(related), name + "." + childName, child.PropertyType));
            }
        }
        return control;
    }

    private static PropertyInfo? FindBindingProperty(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            PropertyInfo[] matches = current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(property => property.Name == name && property.GetIndexParameters().Length == 0).ToArray();
            if (matches.Length > 1) throw new AmbiguousMatchException("The declared binding property is ambiguous.");
            if (matches.Length == 1) return matches[0];
        }
        return null;
    }

    private static ProbeBinding DescribeBinding(object? context, string name, Type contractType)
    {
        var binding = new ProbeBinding
        {
            Name = name,
            ContextType = context?.GetType().FullName ?? "(none)"
        };
        if (context == null) return binding;
        IEnumerable<PropertyInfo> properties = context.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
        if (contractType.IsInterface)
            properties = contractType.GetProperties().Concat(contractType.GetInterfaces().SelectMany(type => type.GetProperties()))
                .Concat(properties);
        PropertyInfo[] inventory = properties
            .Where(property => property.GetIndexParameters().Length == 0)
            .GroupBy(property => property.Name, StringComparer.Ordinal).Select(group => group.First())
            .OrderBy(property => property.Name, StringComparer.Ordinal).Take(100).ToArray();
        binding.Properties = inventory.Select(property => property.Name + ": " + property.PropertyType.FullName).ToArray();
        foreach (PropertyInfo property in inventory)
        {
            if (property.GetMethod == null || !property.GetMethod.IsPublic) continue;
            if (!(property.Name == "Id" || property.Name == "SessionId" || property.Name == "ConversationId" ||
                property.Name == "ChatId")) continue;
            if (property.PropertyType.IsValueType && property.PropertyType.FullName == "Microsoft.VisualStudio.Copilot.CopilotSessionId")
            {
                object typedValue = property.GetValue(context) ?? throw new InvalidDataException("The typed session identity is unavailable.");
                PropertyInfo guid = RequireIdentityProperty(property.PropertyType, "Guid", typeof(Guid));
                binding.Identities.Add(property.Name + ".Guid=" + guid.GetValue(typedValue));
                PropertyInfo? client = FindBindingProperty(property.PropertyType, "ClientId");
                if (client == null || client.GetMethod == null || !client.GetMethod.IsPublic ||
                    client.PropertyType.FullName != "Microsoft.VisualStudio.Copilot.CopilotClientId")
                    throw new InvalidDataException("The typed session client contract is unsupported.");
                object? clientValue = client.GetValue(typedValue);
                string clientText = clientValue == null ? "(null)" :
                    (string?)RequireIdentityProperty(client.PropertyType, "Id", typeof(string)).GetValue(clientValue) ?? "(null)";
                if (clientText.Length > 256) throw new InvalidDataException("Identity exceeds the probe limit.");
                binding.Identities.Add(property.Name + ".ClientId.Id=" + clientText);
                continue;
            }
            if (property.PropertyType != typeof(string) && property.PropertyType != typeof(Guid)) continue;
            object? value = property.GetValue(context);
            string text = value?.ToString() ?? "(null)";
            if (text.Length > 256) throw new InvalidDataException("Identity exceeds the probe limit.");
            binding.Identities.Add(property.Name + "=" + text);
        }
        return binding;
    }

    private static PropertyInfo RequireIdentityProperty(Type type, string name, Type valueType)
    {
        PropertyInfo? property = FindBindingProperty(type, name);
        if (property == null || property.GetMethod == null || !property.GetMethod.IsPublic || property.PropertyType != valueType)
            throw new InvalidDataException("The typed identity property contract is unsupported.");
        return property;
    }

    private static object ReadObservedBinding(object context, string name)
    {
        PropertyInfo? property = FindBindingProperty(context.GetType(), name);
        if (property == null || property.GetMethod == null || !property.GetMethod.IsPublic)
            throw new InvalidDataException("A required native observation getter is unavailable.");
        return property.GetValue(context) ?? throw new InvalidDataException("A required native observation binding is unavailable.");
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lock (sync)
        {
            activePipe?.Dispose();
            ChatTargetObservation? current = observation;
            observation = null;
            if (current == null) return;
            current.RequestDisposal();
        }
    }

    [DataContract]
    internal sealed class ProbeSnapshot
    {
        internal List<FrameworkElement> NativeTargets { get; } = new List<FrameworkElement>();
        [DataMember] public int Visited { get; set; }
        [DataMember] public List<ProbeControl> Controls { get; set; } = new List<ProbeControl>();
        [DataMember] public string TargetStatus { get; set; } = "NotObserved";
        [DataMember] public ChatTargetObservation.ObservationSnapshot? Target { get; set; }
        [DataMember] public bool? IsReadOnly { get; set; }
        [DataMember] public bool? IsTranscript { get; set; }
    }

    [DataContract]
    internal sealed class ProbeControl
    {
        [DataMember] public string Type { get; set; } = "";
        [DataMember] public string AutomationId { get; set; } = "";
        [DataMember] public string ContextType { get; set; } = "";
        [DataMember] public string[] Properties { get; set; } = Array.Empty<string>();
        [DataMember] public List<string> Identities { get; set; } = new List<string>();
        [DataMember] public List<ProbeBinding> Bindings { get; set; } = new List<ProbeBinding>();
    }

    [DataContract]
    internal sealed class ProbeBinding
    {
        [DataMember] public string Name { get; set; } = "";
        [DataMember] public string ContextType { get; set; } = "";
        [DataMember] public string[] Properties { get; set; } = Array.Empty<string>();
        [DataMember] public List<string> Identities { get; set; } = new List<string>();
    }
}
# Key flows

- `Program.Main -> Program.ParseOwner -> ReaderWindow.ReaderWindow -> DocumentBuilder.LoadReply -> DocumentBuilder.ValidateVersions -> ReplyVersionPreference.Load`
- `ReaderWindow.SwitchVersion -> ReaderWindow.CancelSpeech -> ReaderWindow.ClearHighlight -> ReplyVersionPreference.Save -> ReaderWindow.WriteStatus`
- `VoicePackage.InitializeAsync -> JoinableTaskFactory.SwitchToMainThreadAsync -> ChatToolbarIntegration.ChatToolbarIntegration -> DispatcherTimer.Start`
- `ChatToolbarIntegration.Tick -> ChatToolbarIntegration.Refresh -> ChatToolbarIntegration.TryFindHost -> ChatToolbarIntegration.CreateToolbar -> VoicePackage.ConfigureTheme`
- `VoicePackage.Dispose -> ChatToolbarIntegration.Dispose -> DispatcherTimer.Stop -> ChatToolbarIntegration.Detach`
- `VoicePackage.InitializeAsync -> ChatBridgePoc.IsEnabled -> ChatBridgePoc.ChatBridgePoc -> Task.Run -> ChatBridgePoc.RunAsync -> NamedPipeServerStream.WaitForConnectionAsync -> ChatBridgePoc.CaptureAsync -> JoinableTaskFactory.SwitchToMainThreadAsync -> ChatBridgePoc.CaptureOnUiThread -> ChatBridgePoc.Inspect -> ChatBridgePoc.Describe`
- `ChatBridgePoc.RunAsync -> CancellationTokenSource.CancelAfter -> cancellation registration -> NamedPipeServerStream.Dispose -> cancellation check -> per-connection rejection/timeout handling -> next NamedPipeServerStream`
- `VoicePackage.Dispose -> ChatBridgePoc.Dispose -> CancellationTokenSource.Cancel -> NamedPipeServerStream.Dispose`
- `ChatBridgePoc.Describe -> ChatBridgePoc.FindBindingProperty -> ChatBridgePoc.DescribeBinding -> ProbeBinding`
- `ChatBridgePoc.CaptureOnUiThread -> ChatTargetObservation.ChatTargetObservation -> IObservable.Subscribe -> ChatTargetObservation.Snapshot`
- `ChatTargetObservation.VisibilityChanged / Unloaded / ContextChanged / ComposerChanged / StateEnded -> ChatTargetObservation.TryInvalidate -> ChatTargetObservation.QueueCleanup -> ChatBridgePoc.ScheduleOnUiThreadAsync -> ChatTargetObservation.Detach`
- `ChatBridgePoc.Dispose -> ChatTargetObservation.RequestDisposal -> ChatTargetObservation.QueueCleanup -> ChatTargetObservation.Detach`
- `VoicePackage.InitializeAsync -> ChatBridgePoc.IsDraftEnabled -> ChatDraftPoc.ChatDraftPoc`
- `ChatBridgePoc.RunAsync -> ChatBridgePoc.ReadExactlyAsync -> ChatBridgePoc.InsertDraftAsync -> ChatDraftPoc.RejectInsert -> InsertResult (NativeAtomicInsertionUnsupported; no draft access)`
- `Invoke-GuardedChatDraftPoc -> shared deadline -> Write-PocFrame -> WriteAsync / bounded Wait -> ReadAsync / bounded Wait -> acknowledgement; timeout -> actual pipe disposal -> unknown outcome, no retry`
- `ChatBridgePoc.CaptureOnUiThread -> ChatDraftPoc.HasConstructorOnlyBinding -> FieldInfo metadata validation`

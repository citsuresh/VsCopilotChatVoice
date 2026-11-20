# Key flows

- `Program.Main -> Program.ParseOwner -> ReaderWindow.ReaderWindow -> DocumentBuilder.LoadReply -> DocumentBuilder.ValidateVersions -> ReplyVersionPreference.Load`
- `ReaderWindow.SwitchVersion -> ReaderWindow.CancelSpeech -> ReaderWindow.ClearHighlight -> ReplyVersionPreference.Save -> ReaderWindow.WriteStatus`
- `VoicePackage.InitializeAsync -> JoinableTaskFactory.SwitchToMainThreadAsync -> ChatToolbarIntegration.ChatToolbarIntegration -> DispatcherTimer.Start`
- `ChatToolbarIntegration.Tick -> ChatToolbarIntegration.Refresh -> ChatToolbarIntegration.TryFindHost -> ChatToolbarIntegration.CreateToolbar -> VoicePackage.ConfigureTheme`
- `VoicePackage.Dispose -> ChatToolbarIntegration.Dispose -> DispatcherTimer.Stop -> ChatToolbarIntegration.Detach`

# Key flows

- `Program.Main -> Program.ParseOwner -> ReaderWindow.ReaderWindow -> DocumentBuilder.LoadReply -> DocumentBuilder.ValidateVersions -> ReplyVersionPreference.Load`
- `ReaderWindow.SwitchVersion -> ReaderWindow.CancelSpeech -> ReaderWindow.ClearHighlight -> ReplyVersionPreference.Save -> ReaderWindow.WriteStatus`
- `VoicePackage.InitializeAsync -> JoinableTaskFactory.SwitchToMainThreadAsync -> ActivityLog.LogInformation`
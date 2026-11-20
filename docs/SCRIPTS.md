# Agent scripts

| Script | Purpose | How to call | Access | Date |
| --- | --- | --- | --- | --- |
| `scripts\agent\Read-ChatBridgeSurface.ps1` | Bounded, chat-scoped UIA identity/control research; output may contain chat text and must stay local. | `& .\scripts\agent\Read-ChatBridgeSurface.ps1 -Hwnd 0x1234 -MaxDepth 5` | Read-only; no focus/input/scroll changes. | 2026-11-21 |
| `scripts\agent\Read-ChatBridgePoc.ps1` | Read the gated Exp diagnostic bridge with explicit server-PID verification; inspect binding/identity metadata. | `& .\scripts\agent\Read-ChatBridgePoc.ps1 -TargetProcessId 12345` | Read-only; requires the Debug/Exp probe explicitly enabled at VS launch. | 2026-11-21 |
| `scripts\agent\Read-PublicAssemblyContract.ps1` | Inspect exact public .NET Framework type/member metadata and optionally verify a direct constructor-only property slot; does not execute target code or read private field values. | `& .\scripts\agent\Read-PublicAssemblyContract.ps1 -AssemblyPath '<DLL>' -TypeName '<public-type>' -DependencyDirectory '<folder>' [-VerifyConstructorOnlyProperty '<property>']` | Read-only; Windows PowerShell, explicit dependency folders, 32-type/100-member defaults; missing dependencies or excess members fail explicitly. | 2026-11-21 |

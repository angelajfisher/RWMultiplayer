# Apparel policy deletion crash, 2026-11-08

The saved Player-prev.log ends in a native SIGSEGV. Its managed stack runs through
Verse.Messages.AcceptsMessage, Messages.Message_Patch1, and
RimWorld.Dialog_ManagePolicies<T>.DeletePolicy, called by Dialog_Confirm.
The complete original log is retained locally as apparel-crash-20261108.log.

Dialog_ManagePolicies.DeletePolicy calls OutfitDatabase.TryDelete and displays
AcceptanceReport.Reason when the returned report is rejected. Multiplayer queues
TryDelete as a synchronized command and returns a default AcceptanceReport to the UI.

SyncTemplates.CreateDefaultCodes previously emitted ldloca + initobj, but omitted
ldloc before ret. initobj consumes the address; ret therefore had no struct value
on the evaluation stack. This invalid IL can yield a corrupt struct return under
Mono, including a garbage string reference passed to AcceptsMessage. It is a
strong explanation for the observed stack; the live UI crash has not been replayed.

The fix emits ldloc after initialization. Out struct parameters now emit the whole
initialization sequence and stobj instead of taking only its last instruction.
An isolated process running the installed game's x64 Mono checks 10,000 default
AcceptanceReport returns and the actual sync transpiler with both an out struct
and struct return. The existing policy sync commands and deletion logic are retained.
No tracing behavior or network protocol change is needed.

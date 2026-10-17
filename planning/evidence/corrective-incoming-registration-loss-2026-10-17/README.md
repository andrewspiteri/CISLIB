# Incoming registration loss and callback-prefix accounting

17 October 2026, based on d28b9c32. Fixes approved continuation review 22-F1 and 22-F4.

A timed-out incoming flush retains confirmed output unavailability from that caller's registration even when the input prefix is unresolved and Outputs remains empty. An internal result-constructor flag preserves LossObserved without inventing downstream boundaries or counting filtering as loss. Later caller rejections do not change the earlier result.

Each synchronous Prepare invocation now has a distinct callback-scope marker. Inherited flows remain recursion-protected after that marker stops executing. A detached child that logs later increments live caller invalidity, rather than charging the next worker prefix. A child that is joined while its originating callback is active still contributes covered worker invalidity. A fresh marker is necessary: reusing one flag for later Prepare invocations would misattribute the older child's flow again.

Five native C# regressions cover registration before/after timeout, detached rejection before/after registration and a joined child rejection. Two fail before the fixes (false LossObserved and an unrelated later-prefix invalid count of 1); three contrast cases already pass. Raw TRX is retained without line-ending conversion.

All 61 selected incoming/processing/flush/stop unit cases pass per native lane, zero failures/skips: Windows f9e1fa7528a2490b8af4aca3d70f95c5 and Ubuntu 3012fddd667c41618a6c4a47ded0e678. These focused checks do not repeat full Developer, coverage, mutation, compatibility or release gates. Other supported continuation findings are being addressed; the original roadmap stays paused.
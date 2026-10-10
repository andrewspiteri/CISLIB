# Workflow preparation

Add native CI workflows here when their execution lanes are qualified. The initial local entry point is `pwsh -File eng/verify.ps1`. Windows has local scaffold verification; a future Linux run must establish its own evidence.

Release automation must qualify the complete logging verification inventory and exact packages. The scaffold developer check is not a release gate.

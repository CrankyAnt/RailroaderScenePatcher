# Targeted Redo chrome diagnostic

Apply one custom operation, save a checkpoint, undo, then redo once. Log dirty getter calls during native Redo. Inspect title and actual Save interactability before identity transfer, after transfer and after one native RefreshChrome. Reinspect after the deferred UI rebuild settles. Do not save after Redo. No earlier successful failure or divergent-history test is repeated. See [the result](../NativeSaveUndoFeasibility.md) and [raw log](ProbeV4.log).

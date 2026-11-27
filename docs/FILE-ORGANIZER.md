# File Organizer

File Organizer is an optional independent Windows utility. Extract its complete
`FileOrganizer-windows-x64.zip` and run `FileOrganizer.exe`. It requires no running
Aquarium hub or account. Version 1.2 retains reviewed rename/move plans, session
undo and recovery records, and replaces the unbounded duplicate-content scan.

## Compare selected file contents

Choose files and optionally select a subset in the list. **Compare duplicate
contents** uses file lengths and SHA-256 without modifying source files. Files with
unique observed lengths need no content read. Shared-length candidates are hashed
from read-only streams; observed source changes remain explicit. It does not compare
visual similarity, remove files, update file records or upload results. Separate
hard links may already share storage, so matches do not prove reclaimable space.

Limits are 10,000 input paths, 2 MiB of path text, 4,096 characters per path,
256 MiB per file and 4 GiB of actual content reads per operation. Duplicated paths
are not counted as separate matching files. Linked, missing, invalid, inaccessible,
changed, oversized and budget-skipped files remain unknown in a partial report.
A no-match observation does not cover skipped files or other folders. Files may
change after comparison; the report is an observation rather than a deletion plan
or filesystem lock against non-cooperating writers.

Cancel stops without presenting a completed comparison. Closing during comparison
requests cancellation; no mutating file operation occurs. Progress is throttled so
large selections do not enqueue an update for every read. The on-screen review
limits long group/path lists and points to the full explicit JSON export.

**Save new comparison JSON** includes full local paths, observed hashes, UTC time,
fixed result codes, limits and partial-coverage details. Review before sharing.
Choose a new `.json` filename outside the program/profile folders and selected
source paths. Existing files are never overwritten. A same-folder temporary file is
flushed and renamed without overwrite; cancellation removes only that attempt's
temporary file. No report is retained automatically. A completed report is limited
to 32 MiB. Cancel can also stop export before publication.

## Existing rename/move workflow

Choose files, enter a rename prefix or choose a move destination, then inspect the
source/destination preview before Apply. Undo lasts for the current window session.
Review recovery records shows the saved path plan and progress journal under
`%LOCALAPPDATA%\AquariumFileOrganizer\Recovery`. These records are not copies of
file contents and recovery never moves files automatically. Retain independent
backups and inspect both paths after interruption. Comparison/export is separate
from this mutating workflow; it never proposes automatic duplicate removal.

## Validation limits

Generated-file tests cover exact hash groups, path deduplication, missing/locked/
changed files, file/read budgets, cancellation, new-file JSON publication and source
preservation. Portable Linux checks also reject symbolic links. Generated-data
native UI checks cover 100/150/200% fonts, partial/canceled states, result review
and export availability. Pickers, physical displays, screen readers, power loss,
remote filesystem behavior and independent security review remain separate checks.
This utility is not the complete requested tabbed File Manager; browsing, bookmarks,
previews, search and wider filesystem management remain on the overhaul roadmap.

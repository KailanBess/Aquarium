# Text Editor

Text Editor is an optional, independently installable Windows utility. Extract the
complete `TextEditor-windows-x64.zip` and run `TextEditor.exe`. It needs no Aquarium
process or account. It opens local text; it never executes files, installs language
servers, uploads documents or connects to a service.

Open up to 20 tabs, with files limited to 1 MiB. Supported encodings are strict
UTF-8 (with or without BOM) and BOM-marked UTF-16 little/big endian. Invalid input,
NUL-containing binary data and oversized files are refused without changing them.
Use Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Shift+S, Ctrl+W and Ctrl+F for normal editing.
The toolbar offers encoding/newline choices, literal case-insensitive find and word
wrap. Closing an unsaved tab asks whether to save, discard or cancel.

Saving unchanged text with unchanged encoding/newline selection keeps the original
bytes. Mixed newline files remain byte-for-byte unchanged until edited or explicitly
converted. Choose LF, CRLF or CR before saving edits to mixed-newline input, changing
its encoding or saving a mixed-newline copy. Encoding conversion is strict; prior
bytes are preserved when validation or preparation fails.

Save detects an observed external change to the opened file and directs you to
Save as. Save as asks before replacing an existing destination. A complete temporary
file is flushed in the destination folder, checked again and published through a
same-volume replacement. Existing Windows DACLs are applied before document bytes
enter the temporary file; EFS-protected destinations require EFS protection of the
temporary file as well. Metadata merge errors are not ignored. Portable file-model
checks also retain Unix permission bits. Lexical/link checks and change detection
are not a filesystem security sandbox or a lock against non-cooperating writers.
Keep separate backups for disk failure, power loss or concurrent external edits.

## Syntax preview

Enable the read-only syntax preview for basic JSON strings/numbers/literals, XML
markup and INI/CFG keys/sections/comments. It is lexical coloring, not validation.
The preview is separate from editable text so formatting cannot alter undo history.
It supports up to 65,536 characters and 4,096 colored spans; larger documents remain
editable as plain text. High-contrast mode retains system colors. Other extensions
remain plain text. Native Windows editing supplies normal selection, clipboard,
undo/redo and scrolling behavior.

## Encrypted recovery

While the editor is open, changed drafts are scheduled for recovery every two
seconds. The status reports failures and pending edits explicitly; recovery does
not replace an explicit save. Up to 20 records and 64 MiB are retained under
`%LOCALAPPDATA%\Aquarium.TextEditor\Recovery`. Document text, original paths and
source fingerprints are inside encrypted records named by random IDs. No plaintext
recovery fallback is offered. One process holds the recovery writer lease; another
editor can still edit/save files with recovery visibly unavailable.

Recovery uses [Windows user-scoped DPAPI](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)
with interactive prompts disabled and no machine-wide flag. It generally requires
the same Windows user/computer context and is not a portable backup or protection
against software running as that user. Password resets or loss of the Windows
profile can make recovery unavailable. The OS performs the encryption and integrity
check; no custom cryptographic format or password cipher is implemented.

Choose Recovery to review retained drafts. Opening a draft restores unsaved text;
it does not write the original file. Changed/missing originals still require Save as.
Unreadable, tampered, duplicate-field, unsupported and future records remain
preserved until explicitly discarded. Successful saves remove valid older recovery
records. Explicitly discarding a dirty tab also discards its recovery copy; Cancel
keeps both. These records are separate from Aquarium record backups.

## Validation limits

Generated-file tests exercise encoding round trips, cancellation, conflicts,
publication failures, Windows DACL preservation, DPAPI round trips/tamper rejection
and corrupt/future-record preservation. Generated-data native UI checks cover
100/150/200% font sizes, tab editing, preview/undo separation and scrolling layout.
Physical displays, screen readers, interactive pickers, EFS volumes, power-loss
recovery and independent security review remain separate acceptance work. This is
a Windows utility; portable file-model tests do not establish a Linux editor UI.

# Activity log format

New records are UTF-8 JSON Lines, version 2, in the existing daily activity_yyyy-MM-dd.log files. One physical line contains Version, Id (GUID), Timestamp (ISO 8601 with offset), Username, EventId, Detail, MessageId and Arguments. EventId remains the existing uppercase event code. MessageId is an immutable localization key; Arguments contain only JSON scalar values. Detail is opaque data and is not translated. A record uses either Detail or MessageId with Arguments.

Readers accept mixed legacy bracketed records and version 2 JSON records in the same file. Existing logs are not rewritten. Unknown versions, duplicate JSON properties and malformed JSON records are not interpreted as sign-in events; the viewer shows a localized unsupported-record marker. Unknown message keys show a localized detail fallback without hiding valid event identity. Newline/control characters in displayed fields are escaped once, and raw opaque values are preserved.

Writers validate sizes, serialize one line, and use a named mutex while appending. An unterminated previous line is separated before writing the next record. Logging remains best effort and does not interrupt application work on I/O failure. The log is local plaintext diagnostic data, not a cryptographically authenticated audit trail. Do not add passwords, TOTP secrets, recovery codes, tokens or decrypted attachments to event parameters.

Use ActivityLogger.LogMessage for application messages with registered translation keys. Use ActivityLogger.Log for opaque data such as a file or service name. Keep the legacy reader for historical files. Add new message keys without renaming existing keys or changing their parameter meaning.

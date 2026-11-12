# Designer draft saving

Editable node and transition properties update the canvas immediately. Switching selection or
tabs keeps those edits without clicking Apply. Each main or child designer saves its own draft
after an 800 ms pause; manual Save, validation and closing a child also await pending saves.

Only one save request runs per designer. Edits made during that request are sent afterward,
and Saved appears only when the latest revision has reached the server. Failed saves retain
the local draft and display a failure status. Save retries immediately; further edits also
trigger another automatic save. Published versions remain read only.

Connection deletion works from the full circular delete control or Delete/Backspace after
selecting the connection. Keyboard actions stay within the focused main or child designer.
Deleting a connection preserves its nodes; undo restores the connection. Deletion persists in
both workflow XML and the saved canvas.

Version 4 also saves incomplete Condition/Fork/Join settings. A saved draft is not necessarily publishable: use Validate to inspect routing diagnostics. Saves carry a revision derived from both executable XML and presentation state; a stale browser session receives a conflict and must reload instead of silently overwriting newer work. Explicit routing upgrades retain the old XML and designer state in a durable snapshot.

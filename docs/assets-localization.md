---
id: assets-localization
title: Images, languages, and voice
---

Author text in Studio by language. Each display value has a stable key and separate translations. Choose enabled, default, and fallback locales, then use **Sync Unity tables**. The Unity table names still begin with `Princess.` for compatibility. Sync should affect only the current project's tables; check them before shipping. CSV translation import uses `key,locale,text,draft` and validates before applying.

For character, background, or activity images, import licensed files into your own game's `Assets`, then assign their asset GUIDs to the corresponding localized asset entries. UI Toolkit `Image` elements can bind to Character Image, Background Image, or Activity Image. The sample generator creates plain editable placeholder images in the game's folder; replace them with art you have rights to redistribute.

Voice is optional. Import an `AudioClip`, assign its GUID to a dialogue line and locale through Studio or `SetDialogueVoice`, and test playback after Load/Restart and language changes. An empty voice GUID means silence. The host owns the session; a screen should not keep stale clip references after reconnecting.

For Korean text, the candidate template includes an OFL font with its license notice. Its binary provenance is still a release gate. For other languages, choose your own licensed font. Build Addressables content and inspect the target Windows player before shipping localized assets. Text length checks and glyph audits do not replace real layout review.

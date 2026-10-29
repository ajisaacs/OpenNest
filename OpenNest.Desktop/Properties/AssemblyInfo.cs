using System.Runtime.CompilerServices;

// The WinForms app drives undo recording through ChangeScope's internal root/nested factories
// and NestFingerprint, and reports Auto Nest progress through the internal JobEngineNest.
[assembly: InternalsVisibleTo("OpenNest")]
[assembly: InternalsVisibleTo("OpenNest.Desktop.Tests")]

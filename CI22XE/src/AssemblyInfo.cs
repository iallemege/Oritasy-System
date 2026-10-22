using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("OritasyHud")]

#if ORITASY_EDITION_D
[assembly: SuppressIldasm]
[module: SuppressIldasm]
[assembly: AssemblyTitle("RM278")]
[assembly: AssemblyProduct("RM278")]
#else
[assembly: AssemblyTitle("Oritasy")]
[assembly: AssemblyProduct("Oritasy")]
#endif
[assembly: AssemblyVersion("2.12.6.0")]
[assembly: AssemblyFileVersion("2.12.6.0")]
#if ORITASY_EDITION_D
[assembly: AssemblyInformationalVersion("0.0.9.240C")]
#else
[assembly: AssemblyInformationalVersion("0.0.9.240C")]
#endif
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.None)]
[assembly: AssemblyDescription("")]

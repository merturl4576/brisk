# Puts the primary display back on the refresh rate the plant recorded, through
# the same P/Invoke that took it off.
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\display-refresh.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }

# One P/Invoke block, byte-identical in the plant and the restore. Both guard
# with -as [type], so a session that ran one script and then the other would
# keep whichever definition loaded first: if the two blocks disagreed about
# their members, the second script would die on a method the first never
# defined. The name carries a version for the same reason - change the members
# below and bump it in both files together.
if (-not ('Brisk.Workbench.DisplayV1' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace Brisk.Workbench {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct DEVMODEV1 {
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
    public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
    public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
    public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
    public short dmLogPixels;
    public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
    public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
    public int dmPanningWidth, dmPanningHeight;
  }

  public static class DisplayV1 {
    public const int EnumCurrentSettings = -1;
    // DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY
    public const int ModeFields = 0x40000 | 0x80000 | 0x100000 | 0x400000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODEV1 devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettings(ref DEVMODEV1 devMode, int flags);

    public static DEVMODEV1 Current() {
      var mode = new DEVMODEV1();
      mode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODEV1));
      if (!EnumDisplaySettings(null, EnumCurrentSettings, ref mode))
        throw new InvalidOperationException("EnumDisplaySettings could not read the current mode");
      return mode;
    }

    /// Every mode the primary display advertises, in enumeration order.
    public static DEVMODEV1[] All() {
      var modes = new System.Collections.Generic.List<DEVMODEV1>();
      for (int i = 0; ; i++) {
        var mode = new DEVMODEV1();
        mode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODEV1));
        if (!EnumDisplaySettings(null, i, ref mode)) break;
        modes.Add(mode);
      }
      return modes.ToArray();
    }

    /// Applies a refresh rate to the primary display for this session only.
    /// Returns the DISP_CHANGE_* code; 0 is DISP_CHANGE_SUCCESSFUL.
    public static int SetHz(int hz) {
      var mode = Current();
      mode.dmDisplayFrequency = hz;
      mode.dmFields = ModeFields;
      return ChangeDisplaySettings(ref mode, 0);
    }
  }
}
'@
}

$prior = Get-Content $state -Raw | ConvertFrom-Json
$result = [Brisk.Workbench.DisplayV1]::SetHz([int] $prior.hz)
# The state file stays on disk while the screen is still wrong: it is the only
# record of the rate to go back to, and deleting it would strand the display.
if ($result -ne 0) { throw "ChangeDisplaySettings refused $($prior.hz) Hz (DISP_CHANGE code $result)" }
Remove-Item $state
Write-Host "restored: primary display -> $($prior.hz) Hz"

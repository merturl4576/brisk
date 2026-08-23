# display-refresh fires when a display runs at least 10 Hz below the highest
# rate its panel advertises. Unlike every other scenario here, this one is
# visible: the screen blanks for a moment and comes back slower. So it asks
# first, and it asks in words you have to type.
#
# The mode is applied for this session only (ChangeDisplaySettings with no
# CDS_UPDATEREGISTRY), the same restraint brisk's own fix shows: nothing is
# written to the registry, so a reboot undoes the plant even if the restore
# script is never run.
#
# Only the primary display is touched. brisk's rule looks at every display, so
# on a multi-monitor desk a second panel already below its rate keeps the
# finding alive after the restore - see the README.
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\display-refresh.json'
if (Test-Path $state) { throw 'state file exists - restore first (double plant would lose the true original)' }

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

$current = [Brisk.Workbench.DisplayV1]::Current()
$gap = 10   # DisplayRefreshRule.MinimumGapHz - below this a gap is unit rounding.

# EnumDisplaySettings uses dmDisplayFrequency 0 and 1 as sentinels meaning "the
# hardware default", not as rates. Reading one as a number is how a script ends
# up announcing "120 Hz -> 1 Hz" and applying the default instead.
if ($current.dmDisplayFrequency -le 1) {
    throw ("the current mode reports a hardware-default refresh rate " +
           "($($current.dmDisplayFrequency)), not a real one - nothing to step down from")
}

$target = [Brisk.Workbench.DisplayV1]::All() |
    Where-Object {
        $_.dmPelsWidth -eq $current.dmPelsWidth -and
        $_.dmPelsHeight -eq $current.dmPelsHeight -and
        $_.dmBitsPerPel -eq $current.dmBitsPerPel -and
        $_.dmDisplayFrequency -gt 1 -and
        $_.dmDisplayFrequency -le ($current.dmDisplayFrequency - $gap)
    } |
    Sort-Object dmDisplayFrequency -Descending |
    Select-Object -First 1

if ($null -eq $target) {
    throw ("no mode at $($current.dmPelsWidth)x$($current.dmPelsHeight) is at least " +
           "$gap Hz below the current $($current.dmDisplayFrequency) Hz - " +
           'this display cannot demonstrate the rule')
}

Write-Host ''
Write-Host 'This scenario CHANGES WHAT YOU SEE.'
Write-Host ("  primary display : $($current.dmPelsWidth)x$($current.dmPelsHeight) " +
            "$($current.dmBitsPerPel)-bit")
Write-Host "  refresh rate    : $($current.dmDisplayFrequency) Hz  ->  $($target.dmDisplayFrequency) Hz"
Write-Host '  the screen will blank for a moment and come back slower'
Write-Host '  nothing is written to the registry, so a reboot undoes this too'
Write-Host ''
$answer = Read-Host 'Type evet (or yes) to plant this'
if ($answer -notin @('evet', 'yes')) { Write-Host 'nothing planted.'; exit 1 }

New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
@{
    hz     = [int] $current.dmDisplayFrequency
    width  = [int] $current.dmPelsWidth
    height = [int] $current.dmPelsHeight
} | ConvertTo-Json | Set-Content $state -Encoding ascii

$result = [Brisk.Workbench.DisplayV1]::SetHz([int] $target.dmDisplayFrequency)
if ($result -ne 0) {
    # No change happened, so the state file would be a record of a plant that
    # never was - and the next plant would refuse for no reason.
    Remove-Item $state
    throw "ChangeDisplaySettings refused the mode (DISP_CHANGE code $result)"
}
Write-Host "planted: primary display $($current.dmDisplayFrequency) Hz -> $($target.dmDisplayFrequency) Hz"

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Sentravane.Sensor.Service.Services;

public static class UserProfileLocator
{
    public static List<string> GetInteractiveUserFolders()
    {
        var result = new List<string>();
        using var profileList = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        if (profileList == null) return result;

        foreach (var subKeyName in profileList.GetSubKeyNames())
        {
            // Filter well-known system SIDs (S-1-5-18 LocalSystem, 19 LocalService, 20 NetworkService)
            if (subKeyName.StartsWith("S-1-5-18") || subKeyName.StartsWith("S-1-5-19") || subKeyName.StartsWith("S-1-5-20"))
                continue;

            using var subKey = profileList.OpenSubKey(subKeyName);
            var profilePath = subKey?.GetValue("ProfileImagePath") as string;
            
            if (!string.IsNullOrEmpty(profilePath) && Directory.Exists(profilePath))
            {
                // Check for standard interactive user folders
                var docs = Path.Combine(profilePath, "Documents");
                var desktop = Path.Combine(profilePath, "Desktop");
                
                if (Directory.Exists(docs)) result.Add(docs);
                if (Directory.Exists(desktop)) result.Add(desktop);
            }
        }
        return result;
    }
}

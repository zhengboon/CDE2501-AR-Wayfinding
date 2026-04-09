using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace CDE2501.Wayfinding.Data
{
    public class CrashReporter : MonoBehaviour
    {
        private static string CrashDir => Path.Combine(Application.persistentDataPath, "Crashes");
        private static string LastCrashFile => Path.Combine(CrashDir, "last_crash.txt");
        public string LastCrashLog { get; private set; }
        public bool HasPendingCrash { get; private set; }

        private void Awake()
        {
            Application.logMessageReceived += HandleLogMessage;
            LoadPendingCrash();
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= HandleLogMessage;
        }

        private void HandleLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;

            try
            {
                if (!Directory.Exists(CrashDir))
                {
                    Directory.CreateDirectory(CrashDir);
                }

                string timestamp = DateTime.UtcNow.ToString("o");
                string device = SystemInfo.deviceModel;
                string os = SystemInfo.operatingSystem;

                var sb = new StringBuilder();
                sb.AppendLine($"[{timestamp}] {type}: {condition}");
                sb.AppendLine($"Device: {device} | OS: {os}");
                sb.AppendLine($"Stack: {stackTrace}");
                sb.AppendLine("---");

                File.AppendAllText(LastCrashFile, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Best effort — don't crash the crash reporter
            }
        }

        private void LoadPendingCrash()
        {
            if (!File.Exists(LastCrashFile))
            {
                HasPendingCrash = false;
                return;
            }

            try
            {
                LastCrashLog = File.ReadAllText(LastCrashFile, Encoding.UTF8);
                HasPendingCrash = !string.IsNullOrWhiteSpace(LastCrashLog);
            }
            catch
            {
                HasPendingCrash = false;
            }
        }

        public void ClearPendingCrash()
        {
            HasPendingCrash = false;
            LastCrashLog = null;
            try
            {
                if (File.Exists(LastCrashFile))
                {
                    File.Delete(LastCrashFile);
                }
            }
            catch { /* best effort */ }
        }

        public string GetCrashFilePath()
        {
            return File.Exists(LastCrashFile) ? LastCrashFile : null;
        }
    }
}

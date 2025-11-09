using System;
using System.Globalization;
using UnityEngine;

namespace GPSBridgeHeadless
{
    /// Minimal parser for GPRMC/GNRMC and GPGGA/GNGGA
    public static class NmeaParser
    {
        public struct Fix
        {
            public bool valid;
            public double latitude;
            public double longitude;
            public double altitudeMeters;
            public int satellites;
            public double hdop;
            public double speedKnots;
            public double courseDeg;
            public DateTime utcTime;
            public string lastSentence;
        }

        public static Fix Parse(string sentence, Fix prev)
        {
            var fix = prev;
            fix.lastSentence = sentence;

            try
            {
                if (sentence.StartsWith("$GPRMC") || sentence.StartsWith("$GNRMC"))
                {
                    var p = sentence.Split(',');
                    // $GPRMC,hhmmss.sss,A,llll.ll,a,yyyyy.yy,a,spd,crs,ddmmyy,...
                    if (p.Length > 9 && p[2] == "A")
                    {
                        fix.valid = true;
                        fix.latitude  = DegMinToDecimal(p[3], p[4]);
                        fix.longitude = DegMinToDecimal(p[5], p[6]);
                        fix.speedKnots = ParseDouble(p[7]);
                        fix.courseDeg  = ParseDouble(p[8]);
                        fix.utcTime    = ParseRmcDateTime(p[1], p[9]);
                    }
                }
                else if (sentence.StartsWith("$GPGGA") || sentence.StartsWith("$GNGGA"))
                {
                    var p = sentence.Split(',');
                    if (p.Length > 9)
                    {
                        int quality = SafeInt(p[6]); // 0 = invalid
                        if (quality > 0)
                        {
                            fix.valid = true;
                            fix.latitude  = DegMinToDecimal(p[2], p[3]);
                            fix.longitude = DegMinToDecimal(p[4], p[5]);
                            fix.satellites = SafeInt(p[7]);
                            fix.hdop = ParseDouble(p[8]);
                            fix.altitudeMeters = ParseDouble(p[9]);
                            fix.utcTime = ParseGgaTime(p[1], fix.utcTime.Date);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"NMEA parse error: {e.Message} | '{sentence}'");
            }

            return fix;
        }

        static double ParseDouble(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0;

        static int SafeInt(string s) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        static double DegMinToDecimal(string dm, string hemi)
        {
            if (string.IsNullOrEmpty(dm)) return 0;
            double raw = ParseDouble(dm);
            int deg = (int)(raw / 100);
            double min = raw - (deg * 100);
            double dec = deg + (min / 60.0);
            if (hemi == "S" || hemi == "W") dec = -dec;
            return dec;
        }

        static DateTime ParseRmcDateTime(string hhmmss, string ddmmyy)
        {
            try
            {
                int h = int.Parse(hhmmss.Substring(0,2));
                int m = int.Parse(hhmmss.Substring(2,2));
                int s = int.Parse(hhmmss.Substring(4,2));
                int d = int.Parse(ddmmyy.Substring(0,2));
                int mo = int.Parse(ddmmyy.Substring(2,2));
                int y = int.Parse(ddmmyy.Substring(4,2)) + 2000;
                return new DateTime(y, mo, d, h, m, s, DateTimeKind.Utc);
            }
            catch { return DateTime.UtcNow; }
        }

        static DateTime ParseGgaTime(string hhmmss, DateTime currentDateUtc)
        {
            try
            {
                int h = int.Parse(hhmmss.Substring(0,2));
                int m = int.Parse(hhmmss.Substring(2,2));
                int s = int.Parse(hhmmss.Substring(4,2));
                return new DateTime(currentDateUtc.Year, currentDateUtc.Month, currentDateUtc.Day, h, m, s, DateTimeKind.Utc);
            }
            catch { return DateTime.UtcNow; }
        }
    }
}

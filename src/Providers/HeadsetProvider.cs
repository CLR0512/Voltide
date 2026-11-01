using System;
using System.Collections.Generic;
using Voltide.Core;
using Voltide.Interop;

namespace Voltide.Providers
{
    /// <summary>
    /// Razer BlackShark V2 Pro (and siblings on the same audio receiver).
    ///
    /// Unlike the mouse receivers this dongle exposes a 64-byte vendor
    /// collection (usage page 0xFF00) with report id 0x02 and a
    /// "50 41 / 50 49" request-reply framing:
    ///
    ///   host -> dongle   02 80 09 00 00 50 41 08 00 04 21 00 01 01
    ///   dongle -> host   02 0E 50 49 08 .. .. .. .. .. .. 21 01 01 4B
    ///                                                        ^^    ^^
    ///                                                        |     +-- value (0x4B = 75 %)
    ///                                                        +-------- field id 0x21 = battery
    ///
    /// Field 0x2A answers the charging flag (0x01 while charging).
    /// Frames on this channel are 64 bytes in both directions; replies arrive
    /// on the interrupt IN endpoint, so a plain write/read pair is enough.
    /// </summary>
    public class HeadsetProvider : IDeviceProvider
    {
        private const ushort RazerVid = 0x1532;
        private const ushort BlackSharkPid = 0x0555;
        private const int ReportLength = 64;
        private const byte FieldBattery = 0x21;
        private const byte FieldCharging = 0x2A;

        public string Name { get { return "Razer Audio"; } }

        public List<DeviceInfo> Discover(List<HidDeviceInfo> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();
            Dictionary<string, HidDeviceInfo> candidates = new Dictionary<string, HidDeviceInfo>();

            foreach (HidDeviceInfo h in catalog)
            {
                if (h.VendorId != RazerVid) continue;
                if (h.UsagePage != 0xFF00) continue;
                if (h.InputReportLength != ReportLength) continue;
                if (h.OutputReportLength != ReportLength) continue;
                if (h.ProductId == 0x007E) continue;          // mouse dock shares the shape

                string key = h.ProductId.ToString("X4");
                if (!candidates.ContainsKey(key)) candidates.Add(key, h);
            }

            foreach (KeyValuePair<string, HidDeviceInfo> kv in candidates)
            {
                HidDeviceInfo h = kv.Value;
                DeviceInfo d = new DeviceInfo();
                d.Id = "headset-" + kv.Key;
                d.Brand = "Razer";
                d.Model = ResolveModel(h.ProductId, h.ProductName);
                d.Name = d.Model;
                d.ShortModel = "黑鲨 V2 Pro";
                d.Kind = DeviceKind.Headset;
                d.VendorId = h.VendorId;
                d.ProductId = h.ProductId;
                d.HidPath = h.Path;
                d.ProviderName = Name;
                found.Add(d);
            }

            return found;
        }

        public DeviceReading Read(DeviceInfo d)
        {
            DeviceReading r = new DeviceReading();
            r.UpdatedAt = DateTime.Now;
            r.Online = false;
            r.Level = BatteryLevel.Unknown;
            r.Detail = "2.4G · 雷蛇音频";

            try
            {
                using (HidSession session = new HidSession(d.HidPath, ReportLength, ReportLength))
                {
                    session.Flush(40);

                    int? battery = Query(session, FieldBattery);
                    if (!battery.HasValue)
                    {
                        r.Sleeping = true;
                        r.Error = "耳机未应答（可能已关机）";
                        return r;
                    }

                    int level = battery.Value;
                    if (level > 100) level = 100;
                    if (level < 0) level = 0;

                    r.Percent = level;
                    r.Level = BatteryMath.FromPercent(level);
                    r.Online = true;
                    r.Detail = "2.4G · 雷蛇音频";

                    int? charge = Query(session, FieldCharging);
                    if (charge.HasValue) r.Charging = charge.Value != 0;
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }

            return r;
        }

        private static int? Query(HidSession session, byte field)
        {
            byte[] request = new byte[ReportLength];
            request[0] = 0x02;
            request[1] = 0x80;
            request[2] = 0x09;
            request[5] = 0x50;
            request[6] = 0x41;
            request[7] = 0x08;
            request[9] = 0x04;
            request[10] = field;
            request[12] = 0x01;
            request[13] = 0x01;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!session.Write(request, 200)) continue;

                for (int i = 0; i < 3; i++)
                {
                    byte[] reply = session.Read(150);
                    if (reply == null) break;
                    if (reply.Length < 16) continue;
                    if (reply[0] != 0x02) continue;
                    if (reply[2] != 0x50 || reply[3] != 0x49) continue;
                    if (reply[12] != field) continue;
                    if (reply[13] != 0x01 || reply[14] != 0x01) continue;
                    return reply[15];
                }
            }
            return null;
        }

        private static string ResolveModel(ushort pid, string productName)
        {
            switch (pid)
            {
                case BlackSharkPid: return "BLACKSHARK";
                default:
                    string name = productName;
                    if (!string.IsNullOrEmpty(name) && name.StartsWith("Razer "))
                        name = name.Substring(6);
                    if (string.IsNullOrEmpty(name)) name = "雷蛇耳机";
                    return name;
            }
        }
    }
}

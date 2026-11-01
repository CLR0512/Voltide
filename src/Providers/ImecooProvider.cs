using System;
using System.Collections.Generic;
using Voltide.Core;
using Voltide.Interop;

namespace Voltide.Providers
{
    /// <summary>
    /// IMECOO P1 2.4G mouse.
    ///
    /// Dongle: VID 0x3554, PID 0xFB34, product string "IMECOO-P1".
    /// The firmware comes from the same CompX family as the ATK A9 Air, so the
    /// framing is byte-for-byte the same shape; only the ids differ.
    ///
    ///   channel : vendor collection, usage page 0xFF02, input and output report
    ///             length 17. Report id 0x08 is the only writable id on this
    ///             collection (0x01..0x1F were all rejected by SET_REPORT).
    ///
    ///   query : 17 bytes
    ///     [0]      = 0x08  report id
    ///     [1]      = 0x04  opcode: battery query
    ///     [2..15]  = 0x00  body
    ///     [16]     = checksum, sum(frame[1..16]) &amp; 0xFF == 0x4D
    ///                     -> 0x49 for the battery query
    ///
    ///   reply : 17 bytes, read back from the same handle.
    ///           Verified capture (2026-11-01, the mouse at 30%):
    ///
    ///     08 04 00 00 00 02 1E 00 0E 5A 00 00 00 00 00 00 C1
    ///                       ^^ ^^ ^^^^^                  ^^
    ///                       |  |  |                      +-- checksum
    ///                       |  |  +------------------------- millivolts, big endian
    ///                       |  |                             (0x0E5A = 3674 mV)
    ///                       |  +---------------------------- charging: 0x01 yes, 0x00 no
    ///                       +------------------------------- percent (0x1E = 30)
    ///
    ///   The same 0x4D checksum rule applies to the reply, and [2] is a status
    ///   byte (0x00 ok, 0x01 rejected). Five consecutive queries returned the
    ///   identical frame except for the millivolt field, which drifted down as
    ///   the cell discharged — so the value is a live reading, not a constant.
    ///
    ///   IMPORTANT: this query is device-level, so the mouse itself has to be
    ///   awake. A receiver whose mouse is idle will accept the frame and never
    ///   answer; that is reported as Sleeping so the card keeps its last value.
    ///   Earlier probing attempts failed purely because the mouse was asleep.
    ///
    ///   Transport: WriteFile (interrupt OUT) is what the vendor SDK
    ///   (HIDUsb.dll) uses, and it works here; HidD_SetOutputReport also answers
    ///   on this dongle, so it is kept as a fallback.
    ///
    ///   Do NOT use the 8-byte feature report on the 0xFF04 collection
    ///   (06 00 00 64 64 64 BE D7). Its three 0x64 bytes look like the SDK's
    ///   "BatteryValue_%02x%02x%02x", but they stayed at 100 while the real
    ///   reading was 30% — they are capacity constants, not the charge level.
    /// </summary>
    public class ImecooProvider : IDeviceProvider
    {
        private const ushort ImecooVid = 0x3554;
        private const ushort P1Pid = 0xFB34;
        private const ushort VendorPage = 0xFF02;

        private const int FrameSize = 17;
        private const byte ReportId = 0x08;
        private const byte CmdBattery = 0x04;
        private const byte FrameSum = 0x4D;

        private const int PercentIndex = 6;
        private const int ChargingIndex = 7;
        private const int MillivoltIndex = 8;

        public string Name { get { return "IMECOO HID"; } }

        public List<DeviceInfo> Discover(List<HidDeviceInfo> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();
            Dictionary<string, HidDeviceInfo> candidates = new Dictionary<string, HidDeviceInfo>();

            foreach (HidDeviceInfo h in catalog)
            {
                if (h.VendorId != ImecooVid) continue;
                if (h.ProductId != P1Pid) continue;
                if (h.UsagePage != VendorPage) continue;
                if (h.InputReportLength != FrameSize) continue;
                if (h.OutputReportLength != FrameSize) continue;

                string key = h.VendorId.ToString("X4") + ":" + h.ProductId.ToString("X4");
                if (!candidates.ContainsKey(key)) candidates.Add(key, h);
            }

            foreach (KeyValuePair<string, HidDeviceInfo> kv in candidates)
            {
                HidDeviceInfo h = kv.Value;

                DeviceInfo d = new DeviceInfo();
                d.Id = "imecoo-" + kv.Key;
                d.Brand = "IMECOO";
                d.Model = "P1";
                d.Name = "IMECOO P1";
                d.ShortModel = "P1";
                d.Kind = DeviceKind.Mouse;
                d.VendorId = h.VendorId;
                d.ProductId = h.ProductId;
                d.HidPath = h.Path;
                d.ProviderName = Name;
                found.Add(d);
            }
            return found;
        }

        public DeviceReading Read(DeviceInfo device)
        {
            DeviceReading r = new DeviceReading();
            r.UpdatedAt = DateTime.Now;
            r.Online = false;
            r.Level = BatteryLevel.Unknown;

            try
            {
                byte[] cmd = NewQuery();

                using (HidSession s = new HidSession(device.HidPath, FrameSize, FrameSize))
                {
                    s.Flush(30);

                    byte[] resp = null;

                    for (int attempt = 0; attempt < 3 && resp == null; attempt++)
                    {
                        if (attempt > 0) System.Threading.Thread.Sleep(120);

                        // WriteFile is what the vendor SDK uses; fall back to
                        // SET_REPORT on firmware that only answers that way.
                        bool sent = s.Write(cmd, 400) || s.SetOutputReport(cmd);
                        if (!sent)
                        {
                            r.Error = "发送失败（设备被占用？）";
                            return r;
                        }

                        resp = WaitFor(s, ReportId, CmdBattery, 700);
                    }

                    if (resp == null)
                    {
                        r.Sleeping = true;
                        r.Error = "设备休眠";
                        return r;
                    }

                    r.Raw = Hex(resp);

                    if (resp.Length <= MillivoltIndex + 1)
                    {
                        r.Online = true;
                        r.Percent = null;
                        r.Error = "读数异常";
                        return r;
                    }

                    if (resp[2] != 0x00)
                    {
                        // status 0x01 = the dongle rejected the opcode
                        r.Online = true;
                        r.Percent = null;
                        r.Error = "读数异常";
                        return r;
                    }

                    int level = resp[PercentIndex];
                    if (level > 100)
                    {
                        r.Online = true;
                        r.Percent = null;
                        r.Error = "读数异常";
                        return r;
                    }

                    int millivolts = (resp[MillivoltIndex] << 8) | resp[MillivoltIndex + 1];

                    r.Percent = level;
                    r.Level = BatteryMath.FromPercent(level);
                    r.Charging = resp[ChargingIndex] == 0x01;
                    r.Online = true;
                    r.Detail = "2.4G " + millivolts + " mV";
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }

            return r;
        }

        /// <summary>
        /// The 16 payload bytes after the report id only add up to 0x4D mod 256
        /// when the frame is well formed; the checksum is that complement.
        /// </summary>
        private static byte[] NewQuery()
        {
            byte[] cmd = new byte[FrameSize];
            cmd[0] = ReportId;
            cmd[1] = CmdBattery;
            cmd[FrameSize - 1] = Checksum(cmd);
            return cmd;
        }

        private static byte Checksum(byte[] frame)
        {
            int sum = 0;
            for (int i = 1; i < frame.Length - 1; i++) sum += frame[i];
            return (byte)((FrameSum - sum) & 0xFF);
        }

        private static byte[] WaitFor(HidSession s, byte rid, byte cmd, int timeoutMs)
        {
            for (int i = 0; i < 12; i++)
            {
                byte[] p = s.Read(Math.Max(60, timeoutMs / 4));
                if (p == null) return null;
                if (p.Length > MillivoltIndex + 1 && p[0] == rid && p[1] == cmd) return p;
            }
            return null;
        }

        private static string Hex(byte[] b)
        {
            if (b == null) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < b.Length; i++)
            {
                sb.Append(b[i].ToString("X2"));
                sb.Append(' ');
            }
            return sb.ToString().Trim();
        }
    }
}

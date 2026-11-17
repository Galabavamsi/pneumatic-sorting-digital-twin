using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PlcGateway
{
    // Stand-in for the controller (--demo): plays the Kit 3 sequence on a clock so
    // the dashboard can be built and shown without the rig. Values follow
    // PLC/Kit3/Kit3_Sort.st, with shorter times.
    public sealed class DemoSource
    {
        private static readonly (int step, int ms)[] Plastic =
        {
            (10, 1500), (11, 700), (12, 700), (13, 500), (14, 1200), (15, 1200), (16, 1200),
            (17, 1200), (18, 1200), (19, 1500), (20, 1200), (23, 800), (26, 150)
        };

        private static readonly (int step, int ms)[] Metal =
        {
            (10, 1500), (11, 700), (12, 700), (13, 500), (14, 1200), (15, 1200), (16, 1200),
            (17, 1200), (18, 1200), (19, 1500), (20, 1200), (21, 1400), (23, 800), (25, 1400), (26, 150)
        };

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long stepStart;
        private int index;
        private bool metal;
        private int parts;
        private int metalCount;
        private int plasticCount;

        // Commands, as in the PLC program.
        private bool feedExt;
        private bool stampDown;
        private bool slideExt;
        private bool liftUp = true;
        private bool rotTray2;
        private bool vacuum;
        private int material;

        public Dictionary<string, int?> Read(List<string> names)
        {
            (int step, int ms)[] plan = metal ? Metal : Plastic;
            if (clock.ElapsedMilliseconds - stepStart >= plan[index].ms)
            {
                stepStart = clock.ElapsedMilliseconds;
                index++;
                if (index >= plan.Length)
                {
                    index = 0;
                    parts++;
                    if (metal)
                    {
                        metalCount++;
                    }
                    else
                    {
                        plasticCount++;
                    }

                    metal = !metal;
                    material = 0;
                    if (parts >= 999)
                    {
                        parts = metalCount = plasticCount = 0;
                    }
                }
            }

            plan = metal ? Metal : Plastic;
            int step = plan[index].step;
            switch (step)
            {
                case 11: feedExt = true; break;
                case 12: feedExt = false; break;
                case 13: material = metal ? 1 : 2; break;
                case 14: stampDown = true; break;
                case 15: stampDown = false; break;
                case 16: slideExt = true; break;
                case 17: liftUp = false; break;
                case 18: vacuum = true; break;
                case 19: liftUp = true; break;
                case 20: slideExt = false; break;
                case 21: rotTray2 = true; break;
                case 23: vacuum = false; break;
                case 25: rotTray2 = false; break;
            }

            long inStep = clock.ElapsedMilliseconds - stepStart;
            Dictionary<string, int?> all = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
            {
                ["K3_STEP"] = step,
                ["K3_FAULTCODE"] = 0,
                ["K3_MATERIAL"] = material,
                ["K3_HEARTBEAT"] = (int)(clock.ElapsedMilliseconds / 100 % 30000),
                ["K3_ST_AUTORUNNING"] = 1,
                ["K3_ST_FAULTED"] = 0,
                ["K3_PARTSDONE"] = parts,
                ["K3_METALCOUNT"] = metalCount,
                ["K3_PLASTICCOUNT"] = plasticCount,
                ["_IO_EM_DI_00"] = !feedExt && !(step == 12 && inStep < 400) ? 1 : 0,
                ["_IO_EM_DI_01"] = feedExt && inStep > 400 ? 1 : 0,
                ["_IO_EM_DI_02"] = 1,
                ["_IO_EM_DI_03"] = metal && (step == 12 || step == 13) ? 1 : 0,
                ["_IO_EM_DI_09"] = 0,
                ["_IO_EM_DI_10"] = 0,
                ["_IO_EM_DI_11"] = 0,
                ["_IO_EM_DI_12"] = 1,
                ["_IO_EM_DI_13"] = 1,
                ["_IO_EM_DO_00"] = feedExt ? 1 : 0,
                ["_IO_EM_DO_01"] = feedExt ? 0 : 1,
                ["_IO_EM_DO_02"] = stampDown ? 1 : 0,
                ["_IO_EM_DO_03"] = stampDown ? 0 : 1,
                ["_IO_EM_DO_04"] = slideExt ? 0 : 1,
                ["_IO_EM_DO_05"] = slideExt ? 1 : 0,
                ["_IO_EM_DO_06"] = liftUp ? 1 : 0,
                ["_IO_EM_DO_07"] = liftUp ? 0 : 1,
                ["_IO_EM_DO_08"] = rotTray2 ? 0 : 1,
                ["_IO_EM_DO_09"] = rotTray2 ? 1 : 0,
                ["_IO_EM_DO_10"] = vacuum ? 1 : 0,
                ["K3_T_CAMERASETTLE"] = 500,
                ["K3_T_STAMP"] = 3000,
                ["K3_T_SLIDE"] = 3000,
                ["K3_T_LIFT"] = 3000,
                ["K3_T_ROTARY"] = 3000,
                ["K3_T_VACUUM"] = 3000,
                ["K3_T_RELEASE"] = 2000,
                ["K3_CFG_CAMERAONISMETAL"] = 1
            };

            Dictionary<string, int?> result = new Dictionary<string, int?>();
            foreach (string name in names)
            {
                result[name] = all.TryGetValue(name, out int? v) ? v : null;
            }

            return result;
        }
    }
}

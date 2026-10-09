// AUTO-GENERATED from NroUnity Char.cs:7505-7737 (CharInfo[33][4][3]). Do not edit by hand.
namespace NroRig
{
    /// <summary>33 tu the chuan (cf = 0..32) cua Mannequin NRO.</summary>
    public enum NroPose
    {
        Stand_1 = 0,
        Stand_2_Breathing = 1,
        Run_1 = 2,
        Run_2 = 3,
        Run_3 = 4,
        Run_4 = 5,
        Run_5 = 6,
        Jump_Up = 7,
        Fly_Horizontal = 8,
        Kick_1 = 9,
        Kick_2 = 10,
        Kick_High = 11,
        Fall_Down = 12,
        Punch_1_Ground = 13,
        Punch_2_Ground = 14,
        Punch_Ready_Ground = 15,
        Punch_Strike_Ground = 16,
        Charge_Ki = 17,
        Charge_Blast = 18,
        Kame_Charge = 19,
        Kame_Fire = 20,
        Kame_Release = 21,
        Special_Pose = 22,
        Hit_Injured = 23,
        Knockdown_Dead = 24,
        Dash_Teleport = 25,
        Punch_1_Fly = 26,
        Punch_2_Fly = 27,
        Punch_Ready_Fly = 28,
        Punch_Strike_Fly = 29,
        Kame_Charge_Fly = 30,
        Kame_Fire_Fly = 31,
        Kame_Release_Fly = 32
    }

    /// <summary>Slot bo phan: thu tu index trong CharInfo[cf][slot].</summary>
    public enum NroSlot { Head = 0, Leg = 1, Body = 2, Aura = 3 }

    /// <summary>Ma tran CharInfo[33][4][3] = { subIndex, dx, dy } (dy huong LEN, don vi pixel goc).</summary>
    public static class NroPoseTable
    {
        public const int PoseCount = 33;
        public const int SlotCount = 4;

        // Flat: [cf*12 + slot*3 + {0:sub,1:dx,2:dy}]
        private static readonly int[] Data =
        {
            0,-13,34, 1,-8,10, 1,-9,16, 1,-9,45,   // cf=0
            0,-13,35, 1,-8,10, 1,-9,17, 1,-9,46,   // cf=1
            1,-10,33, 2,-10,11, 2,-8,16, 1,-12,49,   // cf=2
            1,-10,32, 3,-12,10, 3,-11,15, 1,-13,47,   // cf=3
            1,-10,34, 4,-8,11, 4,-7,17, 1,-12,47,   // cf=4
            1,-10,34, 5,-12,11, 5,-9,17, 1,-13,49,   // cf=5
            1,-10,33, 6,-10,10, 6,-8,16, 1,-12,47,   // cf=6
            0,-9,36, 7,-5,17, 7,-11,25, 1,-8,49,   // cf=7
            0,-7,35, 0,-18,22, 7,-10,25, 1,-7,48,   // cf=8
            1,-11,35, 10,-3,25, 12,-10,26, 0,0,0,   // cf=9
            1,-11,37, 11,-3,25, 12,-11,27, 0,0,0,   // cf=10
            0,-14,34, 12,-8,21, 9,-7,31, 0,0,0,   // cf=11
            0,-12,35, 8,-5,14, 8,-15,29, 1,-9,49,   // cf=12
            1,-9,34, 9,-12,9, 10,-7,19, 0,0,0,   // cf=13
            1,-13,34, 9,-12,9, 11,-10,19, 0,0,0,   // cf=14
            1,-8,32, 9,-12,9, 2,-6,15, 0,0,0,   // cf=15
            1,-8,32, 9,-12,9, 13,-12,16, 0,0,0,   // cf=16
            0,-10,31, 9,-12,9, 7,-13,20, 0,0,0,   // cf=17
            0,-11,32, 9,-12,9, 8,-15,26, 0,0,0,   // cf=18
            0,-9,33, 9,-12,9, 14,-8,18, 0,0,0,   // cf=19
            0,-11,33, 9,-12,9, 15,-6,19, 0,0,0,   // cf=20
            0,-16,31, 9,-12,9, 9,-8,28, 0,0,0,   // cf=21
            0,-14,34, 1,-8,10, 8,-16,28, 0,0,0,   // cf=22
            0,-8,36, 7,-5,17, 0,-5,25, 0,0,0,   // cf=23
            0,-9,31, 9,-12,9, 0,-6,20, 0,0,0,   // cf=24
            2,-9,36, 13,-5,17, 16,-11,25, 0,0,0,   // cf=25
            1,-9,34, 8,-5,13, 10,-7,19, 0,0,0,   // cf=26
            1,-13,34, 8,-5,13, 11,-10,19, 0,0,0,   // cf=27
            1,-8,32, 8,-5,13, 2,-6,15, 0,0,0,   // cf=28
            1,-8,32, 8,-5,13, 13,-12,16, 0,0,0,   // cf=29
            0,-9,33, 8,-5,13, 14,-8,18, 0,0,0,   // cf=30
            0,-11,33, 8,-5,13, 15,-6,19, 0,0,0,   // cf=31
            0,-16,32, 8,-5,13, 9,-8,29, 0,0,0,   // cf=32
        };

        public static int Sub(int cf, NroSlot s) { return Data[cf * 12 + (int)s * 3]; }
        public static int Dx(int cf, NroSlot s)  { return Data[cf * 12 + (int)s * 3 + 1]; }
        public static int Dy(int cf, NroSlot s)  { return Data[cf * 12 + (int)s * 3 + 2]; }
    }
}

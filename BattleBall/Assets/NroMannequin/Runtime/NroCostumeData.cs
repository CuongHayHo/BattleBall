using System;
using System.Collections.Generic;
using UnityEngine;

namespace NroRig
{
    /// <summary>
    /// Phân loại 6 nhóm hình thể nhân vật theo giải phẫu pixel và offset dy của đầu.
    /// </summary>
    public enum NroCostumeGroup
    {
        Standard = 1,   // Chuẩn người thường (dy -6..+1, ch=38..45)
        Fusion = 2,     // Hợp thể Potara / Fusion Dance (dy -13..-7, ch=60)
        Broly = 3,      // Khổng lồ / Broly (dy <= -14, ch=58..69+)
        GreatApe = 4,   // Biến khỉ đột Oozaru (dy=-20, ch=60)
        Chibi = 5,      // Thấp bé / Chibi (dy +2..+11, ch=30..38)
        Component = 6   // Cải trang đơn bộ phận / Chỉ có đầu (Body=-1, Leg=-1)
    }

    /// <summary>
    /// Một phần tử frame cắt lát trong Part (tương ứng PartImage của NRO).
    /// </summary>
    [Serializable]
    public class NroPartImageEntry
    {
        public int subIndex;
        public int iconId;
        public int dx;
        public int dy;
    }

    /// <summary>
    /// Dữ liệu cấu trúc của 1 bộ phận (Part) trích xuất từ bảng part trong CSDL.
    /// Type trong SQL: 0 = Head, 1 = Body, 2 = Leg.
    /// </summary>
    [Serializable]
    public class NroPartEntry
    {
        public int partId;
        public int type;
        public List<NroPartImageEntry> images = new List<NroPartImageEntry>();

        public bool TryGetImage(int subIndex, out NroPartImageEntry image)
        {
            if (images != null && subIndex >= 0 && subIndex < images.Count)
            {
                image = images[subIndex];
                return true;
            }
            image = null;
            return false;
        }

        public NroSlot GetSlot()
        {
            switch (type)
            {
                case 0: return NroSlot.Head;
                case 1: return NroSlot.Body;
                case 2: return NroSlot.Leg;
                default: return NroSlot.Head;
            }
        }
    }

    /// <summary>
    /// Dữ liệu cấu trúc của 1 bộ trang phục (Costume) trong danh mục 414 trang phục.
    /// </summary>
    [Serializable]
    public class NroCostumeEntry
    {
        public int id;
        public string name;
        public int gender;          // 0: Trái Đất, 1: Namếc, 2: Xayda, 3: Mọi Giới Tính
        public string genderName;
        public int iconId;          // Item icon hiển thị trong danh sách UI

        public int headPartId = -1;
        public int bodyPartId = -1;
        public int legPartId = -1;

        public string bodyGroup;    // "Standard", "Fusion", "Broly", "GreatApe", "Chibi", "Component"
        public int bodyGroupId = 1; // 1..6

        public bool isHeadOnly;
        public bool isMultiFrameHead;
        public int[] headCyclePartIds;

        public int dyHead;
        public int charHeight = 44;
        public int hpBarOffsetY = 49;

        // PascalCase compatibility properties
        public int Id => id;
        public string Name => name;
        public int Gender => gender;
        public string GenderName => genderName;
        public int IconId => iconId;
        public int HeadPartId => headPartId;
        public int BodyPartId => bodyPartId;
        public int LegPartId => legPartId;
        public string BodyGroup => bodyGroup;
        public int BodyGroupId => bodyGroupId;
        public int CategoryId => bodyGroupId;
        public string CategoryKey => !string.IsNullOrEmpty(bodyGroup) ? bodyGroup : "Standard";
        public bool IsHeadOnly => isHeadOnly;
        public bool IsMultiFrameHead => isMultiFrameHead;
        public int[] HeadCyclePartIds => headCyclePartIds;
        public int DyHead => dyHead;
        public int CharHeight => charHeight;
        public int HeightRuntime => charHeight;
        public int HpBarOffsetY => hpBarOffsetY;

        public string CategoryName
        {
            get
            {
                switch (bodyGroupId)
                {
                    case 1: return "Chuẩn Người Thường";
                    case 2: return "Hợp Thể";
                    case 3: return "Khổng Lồ / Broly";
                    case 4: return "Biến Khỉ Đột";
                    case 5: return "Thấp Bé / Chibi";
                    case 6: return "Cải Trang Đơn Bộ Phận";
                    default: return !string.IsNullOrEmpty(bodyGroup) ? bodyGroup : "Chuẩn";
                }
            }
        }

        public NroCostumeGroup CostumeGroup
        {
            get
            {
                if (bodyGroupId >= 1 && bodyGroupId <= 6)
                    return (NroCostumeGroup)bodyGroupId;
                if (Enum.TryParse<NroCostumeGroup>(bodyGroup, true, out var grp))
                    return grp;
                return NroCostumeGroup.Standard;
            }
        }
    }

    /// <summary>
    /// Lớp bọc phục vụ deserialization JSON bằng Unity JsonUtility.
    /// </summary>
    [Serializable]
    public class NroCostumeCatalogData
    {
        public int totalCostumes;
        public List<NroCostumeEntry> costumes = new List<NroCostumeEntry>();
    }

    /// <summary>
    /// Lớp bọc phục vụ deserialization JSON bằng Unity JsonUtility.
    /// </summary>
    [Serializable]
    public class NroPartDatabaseData
    {
        public int totalParts;
        public List<NroPartEntry> parts = new List<NroPartEntry>();
    }
}

using NroRig;
using UnityEngine;

namespace BattleBall.Net
{
    /// <summary>
    /// The small picture that stands for a costume in the menu (the portrait, the bag, the popups, the mails): the head of the costume as it is drawn while the character
    /// stands, i.e. the first head of the costume. The costume table's icon id (<c>iconId</c>) is not used because for the costumes with a moving head (the fusion
    /// forms) it is a picture that none of the head frames uses (owner report 2026-10-08: the TD form's 15621 and 15622 are single pictures outside the frames); for every
    /// other costume the two are the same picture.
    /// </summary>
    public static class BbCostumeAvatar
    {
        public static Sprite Of(int costumeId)
        {
            var catalog = NroCostumeCatalog.Instance;
            var entry = catalog != null && costumeId > 0 ? catalog.GetCostume(costumeId) : null;
            if (entry == null) return null;
            int partId = entry.HeadCyclePartIds != null && entry.HeadCyclePartIds.Length > 0 ? entry.HeadCyclePartIds[0] : entry.HeadPartId;
            if (partId >= 0)
            {
                var part = catalog.GetPart(partId, BbAssetCache.Scale);
                NroPartFrame frame;
                if (part != null && part.TryGetFrame(NroPoseTable.Sub((int)NroPose.Stand_1, NroSlot.Head), out frame) && frame.sprite != null) return frame.sprite;
            }
            // the head cannot be drawn (its picture is not downloaded yet): the table's own icon is the same picture for an ordinary costume, but for a costume with a moving
            // head it is a picture that no head frame uses (the TD fusion form: icon 15622 and the big 15621), so there nothing is better than that picture
            if (entry.IsMultiFrameHead) return null;
            var resolver = NroSpriteResolver.Instance;
            return resolver != null ? resolver.ResolveSprite(entry.iconId, BbAssetCache.Scale) : null;
        }
    }
}

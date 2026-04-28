using System.Collections.Generic;
using UnityEngine;

namespace LudoFriends.Services
{
    [CreateAssetMenu(fileName = "DiceSkinDatabase", menuName = "Rollmates/Dice Skin Database", order = 1)]
    public class DiceSkinDatabase : ScriptableObject
    {
        [Tooltip("Tüm skin'lerin listesi. Sıra inventory'de görünme sırasıdır.")]
        public List<DiceSkin> skins = new List<DiceSkin>();

        [Tooltip("Yeni oyuncuya verilen varsayılan skin id'si. Bu skin Free tipinde olmalı.")]
        public string defaultSkinId = "default";

        public DiceSkin GetById(string id)
        {
            if (string.IsNullOrEmpty(id) || skins == null) return null;
            foreach (var s in skins)
            {
                if (s != null && s.id == id) return s;
            }
            return null;
        }

        public DiceSkin GetDefault()
        {
            var def = GetById(defaultSkinId);
            if (def != null) return def;
            return skins != null && skins.Count > 0 ? skins[0] : null;
        }
    }
}

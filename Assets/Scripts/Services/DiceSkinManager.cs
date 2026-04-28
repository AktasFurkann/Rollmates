using System.Collections.Generic;
using UnityEngine;

namespace LudoFriends.Services
{
    /// <summary>
    /// Seçili dice skin ve sahip olunan skin'leri yönetir. PlayerPrefs tabanlı.
    /// Database referansı Resources klasöründen lazy load olur (Resources/DiceSkinDatabase.asset).
    /// </summary>
    public static class DiceSkinManager
    {
        private const string PREF_SELECTED = "dice_skin_selected";
        private const string PREF_OWNED = "dice_skin_owned"; // CSV
        private const string PREF_FIRST_LAUNCH = "dice_skin_first_launch_done";
        private const string DATABASE_RESOURCE_PATH = "DiceSkinDatabase";

        public static event System.Action<DiceSkin> OnSelectionChanged;
        public static event System.Action<DiceSkin> OnSkinUnlocked;

        private static DiceSkinDatabase _databaseCache;

        public static DiceSkinDatabase Database
        {
            get
            {
                if (_databaseCache == null)
                {
                    _databaseCache = Resources.Load<DiceSkinDatabase>(DATABASE_RESOURCE_PATH);
                    if (_databaseCache == null)
                        Debug.LogError($"[DiceSkinManager] DiceSkinDatabase not found at Resources/{DATABASE_RESOURCE_PATH}.asset");
                }
                return _databaseCache;
            }
        }

        public static DiceSkin GetSelected()
        {
            EnsureFirstLaunch();
            var db = Database;
            if (db == null) return null;
            string id = PlayerPrefs.GetString(PREF_SELECTED, db.defaultSkinId);
            var skin = db.GetById(id) ?? db.GetDefault();
            return skin;
        }

        public static string GetSelectedId()
        {
            var s = GetSelected();
            return s != null ? s.id : "";
        }

        public static bool Select(string skinId)
        {
            var db = Database;
            if (db == null) return false;
            var skin = db.GetById(skinId);
            if (skin == null)
            {
                Debug.LogWarning($"[DiceSkinManager] Select failed: unknown id '{skinId}'");
                return false;
            }
            if (!IsOwned(skinId))
            {
                Debug.LogWarning($"[DiceSkinManager] Select failed: skin '{skinId}' not owned");
                return false;
            }
            PlayerPrefs.SetString(PREF_SELECTED, skinId);
            PlayerPrefs.Save();
            OnSelectionChanged?.Invoke(skin);
            return true;
        }

        public static bool IsOwned(string skinId)
        {
            EnsureFirstLaunch();
            var owned = GetOwnedSet();
            return owned.Contains(skinId);
        }

        public static IReadOnlyCollection<string> GetOwnedIds()
        {
            EnsureFirstLaunch();
            return GetOwnedSet();
        }

        /// <summary>
        /// Skin'i sahiplenir (kilidi açar). İçeride coin/ad/IAP kontrolü YAPMAZ; çağıran taraf
        /// gerekli ödemeyi/izlemeyi tamamlamış olmalıdır.
        /// </summary>
        public static void Unlock(string skinId)
        {
            var db = Database;
            if (db == null) return;
            var skin = db.GetById(skinId);
            if (skin == null)
            {
                Debug.LogWarning($"[DiceSkinManager] Unlock failed: unknown id '{skinId}'");
                return;
            }
            var owned = GetOwnedSet();
            if (owned.Contains(skinId)) return;
            owned.Add(skinId);
            SaveOwnedSet(owned);
            OnSkinUnlocked?.Invoke(skin);
        }

        private static HashSet<string> GetOwnedSet()
        {
            var csv = PlayerPrefs.GetString(PREF_OWNED, "");
            var set = new HashSet<string>();
            if (string.IsNullOrEmpty(csv)) return set;
            foreach (var part in csv.Split(','))
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed)) set.Add(trimmed);
            }
            return set;
        }

        private static void SaveOwnedSet(HashSet<string> owned)
        {
            PlayerPrefs.SetString(PREF_OWNED, string.Join(",", owned));
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Debug: tüm dice skin state'ini siler. Sonraki erişimde first-launch yeniden çalışır,
        /// sadece default skin owned olur.
        /// </summary>
        public static void DebugReset()
        {
            PlayerPrefs.DeleteKey(PREF_SELECTED);
            PlayerPrefs.DeleteKey(PREF_OWNED);
            PlayerPrefs.DeleteKey(PREF_FIRST_LAUNCH);
            PlayerPrefs.Save();
            Debug.Log("[DiceSkinManager] Debug reset. Next access will re-grant default.");
        }

        private static void EnsureFirstLaunch()
        {
            if (PlayerPrefs.GetInt(PREF_FIRST_LAUNCH, 0) == 1) return;
            var db = Database;
            if (db == null) return;
            var def = db.GetDefault();
            if (def == null)
            {
                Debug.LogError("[DiceSkinManager] Database has no default skin");
                return;
            }
            var owned = new HashSet<string> { def.id };
            SaveOwnedSet(owned);
            PlayerPrefs.SetString(PREF_SELECTED, def.id);
            PlayerPrefs.SetInt(PREF_FIRST_LAUNCH, 1);
            PlayerPrefs.Save();
            Debug.Log($"[DiceSkinManager] First launch: granted default skin '{def.id}'");
        }
    }
}

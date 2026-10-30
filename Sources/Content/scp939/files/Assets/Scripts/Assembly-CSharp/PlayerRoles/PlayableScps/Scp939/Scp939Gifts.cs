using System.Collections.Generic;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>Насколько подарок ей понравился — от этого зависит и реплика, и поведение.</summary>
    public enum Scp939GiftMood : byte
    {
        Neutral = 0,
        Liked = 1,
        Loved = 2,
        Wary = 3,
        Scared = 4
    }

    /// <summary>
    /// Что SCP-939 думает о подаренном предмете.
    ///
    /// Реакции подобраны по лору, а не назначены наугад: она слепа и ориентируется на слух и запах,
    /// поэтому еда и питьё для неё понятны и приятны, а оружие и всё шумное — угроза. Кола (SCP-207)
    /// выделена отдельно: это её любимое, вокруг него и строится свидание.
    ///
    /// Предметы, которых нет в таблице, попадают в <see cref="Scp939GiftMood.Neutral"/> — так новый
    /// предмет в игре не ломает подарки, а просто получает нейтральный ответ.
    /// </summary>
    public static class Scp939Gifts
    {
        private static readonly Dictionary<ItemType, Scp939GiftMood> Moods = new Dictionary<ItemType, Scp939GiftMood>
        {
            // Любимое: мясо — она хищник, это для неё вне конкуренции.

            // Любимое: сладкая газировка — то, ради чего всё и затевалось.
            { ItemType.SCP207, Scp939GiftMood.Loved },
            { ItemType.SCP330, Scp939GiftMood.Loved },

            // Приятное: забота, что-то тёплое или интересное на ощупь.
            { ItemType.Medkit, Scp939GiftMood.Liked },
            { ItemType.SCP500, Scp939GiftMood.Liked },
            { ItemType.Painkillers, Scp939GiftMood.Liked },
            { ItemType.Adrenaline, Scp939GiftMood.Liked },
            { ItemType.Coin, Scp939GiftMood.Liked },
            { ItemType.SCP268, Scp939GiftMood.Liked },
            { ItemType.SCP1853, Scp939GiftMood.Liked },

            // Настораживающее: непонятное, гудящее, чужое.
            // Патроны сами по себе безобидны, но пахнут порохом — для неё это запах оружия.
            { ItemType.Ammo12gauge, Scp939GiftMood.Wary },
            { ItemType.Ammo556x45, Scp939GiftMood.Wary },
            { ItemType.Ammo44cal, Scp939GiftMood.Wary },
            { ItemType.Ammo762x39, Scp939GiftMood.Wary },
            { ItemType.Ammo9x19, Scp939GiftMood.Wary },
            { ItemType.KeycardO5, Scp939GiftMood.Wary },
            { ItemType.Radio, Scp939GiftMood.Wary },
            { ItemType.Flashlight, Scp939GiftMood.Wary },
            { ItemType.SCP018, Scp939GiftMood.Wary },
            { ItemType.SCP2176, Scp939GiftMood.Wary },
            { ItemType.SCP244a, Scp939GiftMood.Wary },
            { ItemType.SCP244b, Scp939GiftMood.Wary },
            { ItemType.SCP1576, Scp939GiftMood.Wary },
            { ItemType.ArmorLight, Scp939GiftMood.Wary },
            { ItemType.ArmorCombat, Scp939GiftMood.Wary },
            { ItemType.ArmorHeavy, Scp939GiftMood.Wary },

            // Пугающее: оружие и взрывчатка. Она слепа — для неё это только резкий звук и опасность.
            { ItemType.GunCOM15, Scp939GiftMood.Scared },
            { ItemType.GunCOM18, Scp939GiftMood.Scared },
            { ItemType.GunCom45, Scp939GiftMood.Scared },
            { ItemType.GunE11SR, Scp939GiftMood.Scared },
            { ItemType.GunCrossvec, Scp939GiftMood.Scared },
            { ItemType.GunFSP9, Scp939GiftMood.Scared },
            { ItemType.GunLogicer, Scp939GiftMood.Scared },
            { ItemType.GunRevolver, Scp939GiftMood.Scared },
            { ItemType.GunAK, Scp939GiftMood.Scared },
            { ItemType.GunShotgun, Scp939GiftMood.Scared },
            { ItemType.MicroHID, Scp939GiftMood.Scared },
            { ItemType.Jailbird, Scp939GiftMood.Scared },
            { ItemType.GrenadeHE, Scp939GiftMood.Scared },
            { ItemType.GrenadeFlash, Scp939GiftMood.Scared },
            { ItemType.ParticleDisruptor, Scp939GiftMood.Scared },

            // Новые предметы проекта: бита и молот Northwood. Обоими замахиваются, поэтому реакция
            // как на джейлбёрд, а не нейтральное «не знаю, что это».
        };

        public static Scp939GiftMood MoodFor(ItemType item)
        {
            if (item.ToString() == "ParticleDisruptorV14" || item.ToString() == "SahurBat") return Scp939GiftMood.Scared;
            if (item.ToString() == "BanHammer") return Scp939GiftMood.Wary;
            if (item.ToString() == "Meat") return Scp939GiftMood.Loved;
            Scp939GiftMood mood;
            return Moods.TryGetValue(item, out mood) ? mood : Scp939GiftMood.Neutral;
        }

        /// <summary>
        /// Ключ реплики на подарок. Вариант 0 — «именная» фраза про этот предмет, остальные — из
        /// набора по настрою. Номер варианта выбирает сервер, поэтому у всех клиентов она говорит
        /// одно и то же, а текст каждый берёт на своём языке.
        /// </summary>
        public static string LineKeyFor(ItemType item, int variant)
        {
            string exact = "gift." + item;
            bool hasExact = Scp939Lines.Exists(exact);
            // Film reviews show the response to this item, not an unrelated mood rotation.
            if (Scp939ModCompatibility.Filming && hasExact) return exact;
            string moodCategory = "mood." + MoodFor(item).ToString().ToLowerInvariant();
            int moodCount = Scp939Lines.Count(moodCategory);

            int total = (hasExact ? 1 : 0) + moodCount;
            if (total == 0)
            {
                return null;
            }

            int index = Mathf.Abs(variant) % total;
            if (hasExact)
            {
                if (index == 0)
                {
                    return exact;
                }
                index--;
            }
            return Scp939Lines.Pick(moodCategory, index);
        }

        /// <summary>
        /// Принимает ли она подарок. Забираем только то, что ей понравилось или хотя бы не
        /// насторожило: раньше «Убери» на патроны и броню всё равно уносило предмет из инвентаря.
        /// </summary>
        public static bool Accepts(ItemType item)
        {
            Scp939GiftMood mood = MoodFor(item);
            return mood == Scp939GiftMood.Loved || mood == Scp939GiftMood.Liked || mood == Scp939GiftMood.Neutral;
        }

        /// <summary>Насколько подарок меняет её симпатию.</summary>
        public static float AffectionDelta(ItemType item)
        {
            switch (MoodFor(item))
            {
                case Scp939GiftMood.Loved:
                    return 18f;
                case Scp939GiftMood.Liked:
                    return 9f;
                case Scp939GiftMood.Neutral:
                    return 2f;
                case Scp939GiftMood.Wary:
                    return -8f;
                default:
                    return -22f;
            }
        }
    }
}

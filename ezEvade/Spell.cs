﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using LeagueSharp;
using LeagueSharp.Common;
using SharpDX;

namespace ezEvade
{
    public class Spell
    {
        public float startTime;
        public float endTime;
        public Vector2 startPos;
        public Vector2 endPos;
        public Vector2 direction;
        public int heroID;
        public int projectileID;
        public SpellData info;
        public int spellID;
        public GameObject spellObject;
        public float evadeTime = float.MinValue;
        public float spellHitTime = float.MinValue;

        public Spell()
        {

        }
    }

    public static class SpellExtensions
    {
        public static float GetSpellRadius(this Spell spell)
        {
            var radius = Evade.menu.SubMenu("Spells").SubMenu(spell.info.charName + spell.info.spellName + "Settings")
                .Item(spell.info.spellName + "SpellRadius").GetValue<Slider>().Value;
            var extraRadius = Evade.menu.SubMenu("MiscSettings").SubMenu("ExtraBuffers").Item("ExtraSpellRadius").GetValue<Slider>().Value;

            return (float)(radius + extraRadius);
        }

        public static int GetSpellDangerLevel(this Spell spell)
        {
            var dangerStr = Evade.menu.SubMenu("Spells").SubMenu(spell.info.charName + spell.info.spellName + "Settings")
                .Item(spell.info.spellName + "DangerLevel").GetValue<StringList>().SelectedValue;

            var dangerlevel = 1;

            switch (dangerStr)
            {
                case "Low":
                    dangerlevel = 1;
                    break;
                case "High":
                    dangerlevel = 3;
                    break;
                case "Extreme":
                    dangerlevel = 4;
                    break;
                default:
                    dangerlevel = 2;
                    break;
            }

            return dangerlevel;
        }

        public static string GetSpellDangerString(this Spell spell)
        {
            switch (spell.GetSpellDangerLevel())
            {
                case 1:
                    return "Low";
                case 3:
                    return "High";
                case 4:
                    return "Extreme";
                default:
                    return "Normal";
            }
        }

        public static Vector2 GetSpellProjection(this Spell spell, Vector2 pos)
        {
            if (spell.info.spellType == SpellType.Line)
            {
                return pos.ProjectOn(spell.startPos, spell.endPos).SegmentPoint;
            }
            else if (spell.info.spellType == SpellType.Circular)
            {
                return spell.endPos;
            }

            return Vector2.Zero;
        }

        public static bool CheckSpellCollision(this Spell spell)
        {
            if (spell.info.collisionObjects.Count() < 1)
            {
                return false;
            }

            List<Obj_AI_Base> collisionCandidates = new List<Obj_AI_Base>();
            var spellPos = spell.GetCurrentSpellPosition();
            var distanceToHero = spellPos.Distance(Evade.myHero.ServerPosition.To2D());

            if (spell.info.collisionObjects.Contains(CollisionObjectType.EnemyChampions))
            {
                foreach (var hero in HeroManager.Allies.Where(h => h.Distance(spellPos) < distanceToHero && !h.IsDead))
                {
                    collisionCandidates.Add(hero);
                }
            }

            if (spell.info.collisionObjects.Contains(CollisionObjectType.EnemyMinions))
            {
                foreach (var minion in ObjectManager.Get<Obj_AI_Minion>()
                    .Where(h=> h.Team == Evade.myHero.Team && h.Distance(spellPos) < distanceToHero && !h.IsDead))
                {
                    collisionCandidates.Add(minion);
                }
            }

            var sortedCandidates = collisionCandidates.OrderBy(h => h.Distance(spellPos));

            foreach (var candidate in sortedCandidates)
            {
                if (EvadeHelper.InSkillShot(spell, candidate.ServerPosition.To2D(), candidate.BoundingRadius))
                {
                    return true;
                }
            }

            return false;
        }

        public static float GetSpellHitTime(this Spell spell, Vector2 pos)
        {

            if (spell.info.spellType == SpellType.Line)
            {
                if (spell.info.projectileSpeed == float.MaxValue)
                {
                    return Math.Max(0, spell.endTime - Evade.GetTickCount() - Game.Ping);
                }

                var spellPos = spell.GetCurrentSpellPosition(true, Game.Ping);
                return 1000 * spellPos.Distance(pos) / spell.info.projectileSpeed;
            }
            else if (spell.info.spellType == SpellType.Circular)
            {
                return Math.Max(0, spell.endTime - Evade.GetTickCount() - Game.Ping);
            }

            return float.MaxValue;
        }

        public static bool CanHeroEvade(this Spell spell, Obj_AI_Base hero, out float rEvadeTime, out float rSpellHitTime)
        {
            var heroPos = hero.ServerPosition.To2D();
            float evadeTime = 0;
            float spellHitTime = 0;

            if (spell.info.spellType == SpellType.Line)
            {
                var projection = heroPos.ProjectOn(spell.startPos, spell.endPos).SegmentPoint;
                evadeTime = 1000 * (spell.GetSpellRadius() - heroPos.Distance(projection) + hero.BoundingRadius) / hero.MoveSpeed;
                spellHitTime = spell.GetSpellHitTime(projection);
            }
            else if (spell.info.spellType == SpellType.Circular)
            {
                evadeTime = 1000 * (spell.GetSpellRadius() - heroPos.Distance(spell.endPos) + hero.BoundingRadius) / hero.MoveSpeed;
                spellHitTime = spell.GetSpellHitTime(heroPos);
            }

            rEvadeTime = evadeTime;
            rSpellHitTime = spellHitTime;

            return spellHitTime > evadeTime;
        }

        public static BoundingBox GetLinearSpellBoundingBox(this Spell spell)
        {
            var myBoundingRadius = Evade.myHero.BoundingRadius;
            var spellDir = spell.direction;
            var pSpellDir = spell.direction.Perpendicular();
            var spellRadius = spell.GetSpellRadius();
            var spellPos = spell.GetCurrentSpellPosition() - spellDir * myBoundingRadius; //leave some space at back of spell
            var endPos = spell.endPos + spellDir * myBoundingRadius; //leave some space at the front of spell

            var startRightPos = spellPos + pSpellDir * (spellRadius + myBoundingRadius);
            var endLeftPos = endPos - pSpellDir * (spellRadius + myBoundingRadius);


            return new BoundingBox(new Vector3(endLeftPos.X, endLeftPos.Y, -1), new Vector3(startRightPos.X, startRightPos.Y, 1));
        }

        public static Vector2 GetCurrentSpellPosition(this Spell spell, bool allowNegative = false, float delay = 0)
        {
            Vector2 spellPos = spell.startPos;

            if (spell.info.spellType == SpellType.Line)
            {
                float spellTime = Evade.GetTickCount() - spell.startTime - spell.info.spellDelay;

                if (spell.info.projectileSpeed == float.MaxValue)
                    return spell.startPos;

                if (spellTime >= 0)
                {
                    spellPos = spell.startPos + spell.direction * spell.info.projectileSpeed * spellTime / 1000;
                }
                else if (allowNegative)
                {
                    spellPos = spell.startPos + spell.direction * spell.info.projectileSpeed * (spellTime / 1000);
                }
            }
            else if (spell.info.spellType == SpellType.Circular)
            {
                spellPos = spell.endPos;
            }

            if (spell.spellObject != null && spell.spellObject.IsValid)
            {
                spellPos = spell.spellObject.Position.To2D();
            }

            if (delay > 0)
            {
                spellPos = spellPos + spell.direction * spell.info.projectileSpeed * (delay / 1000);
            }

            return spellPos;
        }
    }
}

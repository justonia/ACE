using System;
using System.Numerics;

using ACE.Database.Models.Shard;
using ACE.Database.Models.World;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.WorldObjects
{
    public class SpellProjectile : WorldObject
    {
        public Server.Entity.Spell Spell;
        public ProjectileSpellType SpellType { get; set; }

        public Position SpawnPos;
        public float DistanceToTarget { get; set; }
        public uint LifeProjectileDamage { get; set; }

        /// <summary>
        /// A new biota be created taking all of its values from weenie.
        /// </summary>
        public SpellProjectile(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
            SetEphemeralValues();
        }

        /// <summary>
        /// Restore a WorldObject from the database.
        /// </summary>
        public SpellProjectile(Biota biota) : base(biota)
        {
            SetEphemeralValues();
        }

        private void SetEphemeralValues()
        {
            // Override weenie description defaults
            ValidLocations = null;
            DefaultScriptId = null;
        }

        /// <summary>
        /// Perfroms additional set up of the spell projectile based on the spell id or its derived type.
        /// </summary>
        /// <param name="spellId"></param>
        public void Setup(uint spellId)
        {
            Spell = new Server.Entity.Spell(spellId);
            SpellType = GetProjectileSpellType(spellId);

            InitPhysicsObj();

            // Runtime changes to default state
            ReportCollisions = true;
            Missile = true;
            AlignPath = true;
            PathClipped = true;
            if (!Spell.Name.Equals("Rolling Death"))
                Ethereal = false;
            IgnoreCollisions = false;

            if (SpellType == ProjectileSpellType.Bolt || SpellType == ProjectileSpellType.Streak
                || SpellType == ProjectileSpellType.Arc || SpellType == ProjectileSpellType.Volley || SpellType == ProjectileSpellType.Blast
                || WeenieClassId == 7276 || WeenieClassId == 7277 || WeenieClassId == 7279 || WeenieClassId == 7280)
            {
                PhysicsObj.DefaultScript = ACE.Entity.Enum.PlayScript.ProjectileCollision;
                PhysicsObj.DefaultScriptIntensity = 1.0f;
            }

            // Some wall spells don't have scripted collisions
            if (WeenieClassId == 7278 || WeenieClassId == 7281 || WeenieClassId == 7282 || WeenieClassId == 23144)
            {
                ScriptedCollision = false;
            }

            AllowEdgeSlide = false;
            // No need to send an ObjScale of 1.0f over the wire since that is the default value
            if (ObjScale == 1.0f)
                ObjScale = null;

            if (SpellType == ProjectileSpellType.Ring)
            {
                if (spellId == 3818)
                {
                    PhysicsObj.DefaultScript = ACE.Entity.Enum.PlayScript.Explode;
                    PhysicsObj.DefaultScriptIntensity = 1.0f;
                    ScriptedCollision = true;
                }
                else
                {
                    ScriptedCollision = false;
                }
            }
                

            // Whirling Blade spells get omega values and "align path" turned off which
            // creates the nice swirling animation
            if (WeenieClassId == 1636 || WeenieClassId == 7268 || WeenieClassId == 20979)
            {
                AlignPath = false;
                Omega = new Vector3(12.56637f, 0, 0);
            }
        }

        public enum ProjectileSpellType
        {
            Undef,
            Bolt,
            Blast,
            Volley,
            Streak,
            Arc,
            Ring,
            Wall,
            Strike
        }

        public static ProjectileSpellType GetProjectileSpellType(uint spellID)
        {
            var spell = new Server.Entity.Spell(spellID);

            if (spell.Wcid == 0)
                return ProjectileSpellType.Undef;

            if (spell.NumProjectiles == 1)
            {
                if (spell.Category >= SpellCategory.AcidStreak && spell.Category <= SpellCategory.SlashingStreak ||
                         spell.Category == SpellCategory.NetherStreak || spell.Category == SpellCategory.Fireworks)
                    return ProjectileSpellType.Streak;

                else if (spell.NonTracking)
                    return ProjectileSpellType.Arc;

                else if (spell.Name.Contains("Rolling Death"))
                    return ProjectileSpellType.Wall;

                else
                    return ProjectileSpellType.Bolt;
            }

            if (spell.Category >= SpellCategory.AcidRing && spell.Category <= SpellCategory.SlashingRing || spell.SpreadAngle == 360)
                return ProjectileSpellType.Ring;

            if (spell.Category >= SpellCategory.AcidBurst && spell.Category <= SpellCategory.SlashingBurst ||
                spell.Category == SpellCategory.NetherDamageOverTimeRaising3)
                return ProjectileSpellType.Blast;

            // 1481 - Flaming Missile Volley
            if (spell.Category >= SpellCategory.AcidVolley && spell.Category <= SpellCategory.BladeVolley || spell.Name.Contains("Volley"))
                return ProjectileSpellType.Volley;

            if (spell.Category >= SpellCategory.AcidWall && spell.Category <= SpellCategory.SlashingWall)
                return ProjectileSpellType.Wall;

            if (spell.Category >= SpellCategory.AcidStrike && spell.Category <= SpellCategory.SlashingStrike)
                return ProjectileSpellType.Strike;

            return ProjectileSpellType.Undef;
        }

        public float GetProjectileScriptIntensity(ProjectileSpellType spellType)
        {
            if (spellType == ProjectileSpellType.Wall)
            {
                return 0.4f;
            }
            if (spellType == ProjectileSpellType.Ring)
            {
                if (Spell.Level == 6 || Spell.Id == 3818)
                    return 0.4f;
                if (Spell.Level == 7)
                    return 1.0f;
            }

            // Bolt, Blast, Volley, Streak and Arc all seem to use this scale
            // TODO: should this be based on spell level, or power of first scarab?
            // ie. can this use Spell.Formula.ScarabScale?
            switch (Spell.Level)
            {
                case 1:
                    return 0f;
                case 2:
                    return 0.2f;
                case 3:
                    return 0.4f;
                case 4:
                    return 0.6f;
                case 5:
                    return 0.8f;
                case 6:
                case 7:
                case 8:
                    return 1.0f;
                default:
                    return 0f;
            }
        }

        public void ProjectileImpact()
        {
            //Console.WriteLine($"{Name}.ProjectileImpact()");

            ReportCollisions = false;
            Ethereal = true;
            IgnoreCollisions = true;
            NoDraw = true;
            Cloaked = true;
            LightsStatus = false;

            PhysicsObj.set_active(false);

            EnqueueBroadcastPhysicsState();
            EnqueueBroadcast(new GameMessageScript(Guid, ACE.Entity.Enum.PlayScript.Explode, GetProjectileScriptIntensity(SpellType)));

            ActionChain selfDestructChain = new ActionChain();
            selfDestructChain.AddDelaySeconds(5.0);
            selfDestructChain.AddAction(this, () => Destroy());
            selfDestructChain.EnqueueChain();
        }

        /// <summary>
        /// Handles collision with scenery or other static objects that would block a projectile from reaching its target,
        /// in which case the projectile should be removed with no further processing.
        /// </summary>
        public override void OnCollideEnvironment()
        {
            //Console.WriteLine($"{Name}.OnCollideEnvironment()");
            ProjectileImpact();
        }

        public override void OnCollideObject(WorldObject _target)
        {
            //Console.WriteLine($"{Name}.OnCollideObject({_target.Name})");

            var player = ProjectileSource as Player;

            if (player != null)
                player.LastHitSpellProjectile = Spell;

            // ensure valid creature target
            // non-target objects will be excluded beforehand from collision detection
            var target = _target as Creature;
            if (target == null || player == target)
            {
                OnCollideEnvironment();
                return;
            }

            ProjectileImpact();

            // for untargeted multi-projectile war spells launched by monsters,
            // ensure monster can damage target
            var sourceCreature = ProjectileSource as Creature;
            if (sourceCreature != null && !sourceCreature.CanDamage(target))
                return;

            // if player target, ensure matching PK status
            var targetPlayer = target as Player;

            var pkError = CheckPKStatusVsTarget(player, targetPlayer, Spell);
            if (pkError != null)
            {
                if (player != null)
                    player.Session.Network.EnqueueSend(new GameEventWeenieErrorWithString(player.Session, pkError[0], target.Name));

                if (targetPlayer != null)
                    targetPlayer.Session.Network.EnqueueSend(new GameEventWeenieErrorWithString(targetPlayer.Session, pkError[1], ProjectileSource.Name));

                return;
            }

            var critical = false;
            var critDefended = false;
            var damage = CalculateDamage(ProjectileSource, target, ref critical, ref critDefended);

            // null damage -> target resisted; damage of -1 -> target already dead
            if (damage != null && damage != -1)
            {
                // handle void magic DoTs:
                // instead of instant damage, add DoT to target's enchantment registry
                if (Spell.School == MagicSchool.VoidMagic && Spell.Duration > 0)
                {
                    var dot = ProjectileSource.CreateEnchantment(target, ProjectileSource, Spell);
                    if (dot.Message != null && player != null)
                        player.Session.Network.EnqueueSend(dot.Message);

                    // corruption / corrosion playscript?
                    //target.EnqueueBroadcast(new GameMessageScript(target.Guid, ACE.Entity.Enum.PlayScript.HealthDownVoid));
                    //target.EnqueueBroadcast(new GameMessageScript(target.Guid, ACE.Entity.Enum.PlayScript.DirtyFightingDefenseDebuff));
                }
                else
                {
                    DamageTarget(target, damage, critical, critDefended);
                }

                if (player != null)
                    Proficiency.OnSuccessUse(player, player.GetCreatureSkill(Spell.School), Spell.PowerMod);

                // handle target procs
                // note that for untargeted multi-projectile spells,
                // ProjectileTarget will be null here, so procs will not apply
                if (sourceCreature != null && ProjectileTarget != null)
                    sourceCreature.TryProcEquippedItems(target, false);
            }

            // also called on resist
            if (player != null && targetPlayer == null)
                player.OnAttackMonster(target);
        }

        /// <summary>
        /// Calculates the damage for a spell projectile
        /// Used by war magic, void magic, and life magic projectiles
        /// </summary>
        public double? CalculateDamage(WorldObject source, Creature target, ref bool criticalHit, ref bool critDefended)
        {
            var sourcePlayer = source as Player;
            var targetPlayer = target as Player;

            if (source == null || targetPlayer != null && targetPlayer.Invincible == true)
                return null;

            // target already dead?
            if (target.Health.Current <= 0)
                return -1;

            // check lifestone protection
            if (targetPlayer != null && targetPlayer.UnderLifestoneProtection)
            {
                if (sourcePlayer != null)
                    sourcePlayer.Session.Network.EnqueueSend(new GameMessageSystemChat($"The Lifestone's magic protects {targetPlayer.Name} from the attack!", ChatMessageType.Magic));

                targetPlayer.HandleLifestoneProtection();
                return null;
            }

            double damageBonus = 0.0f, warSkillBonus = 0.0f, finalDamage = 0.0f;

            var resistanceType = Creature.GetResistanceType(Spell.DamageType);

            var resisted = source.ResistSpell(target, Spell);
            if (resisted != null && resisted == true)
                return null;

            CreatureSkill attackSkill = null;
            var sourceCreature = source as Creature;
            if (sourceCreature != null)
                attackSkill = sourceCreature.GetCreatureSkill(Spell.School);

            // critical hit
            var critical = GetWeaponMagicCritFrequencyModifier(sourceCreature, attackSkill, target);
            if (ThreadSafeRandom.Next(0.0f, 1.0f) < critical)
            {
                if (targetPlayer != null && targetPlayer.AugmentationCriticalDefense > 0)
                {
                    var criticalDefenseMod = sourcePlayer != null ? 0.05f : 0.25f;
                    var criticalDefenseChance = targetPlayer.AugmentationCriticalDefense * criticalDefenseMod;

                    if (criticalDefenseChance > ThreadSafeRandom.Next(0.0f, 1.0f))
                        critDefended = true;
                }

                if (!critDefended)
                    criticalHit = true;
            }

            var shieldMod = GetShieldMod(target);

            bool isPVP = sourcePlayer != null && targetPlayer != null;

            if (isPVP && Spell.IsHarmful)
                Player.UpdatePKTimers(sourcePlayer, targetPlayer);

            var elementalDmgBonus = GetCasterElementalDamageModifier(sourceCreature, target, Spell.DamageType);

            // Possible 2x + damage bonus for the slayer property
            var slayerBonus = GetWeaponCreatureSlayerModifier(sourceCreature, target);

            // life magic projectiles: ie., martyr's hecatomb
            if (Spell.School == MagicSchool.LifeMagic)
            {
                var lifeMagicDamage = LifeProjectileDamage * Spell.DamageRatio;

                // could life magic projectiles crit?
                // if so, did they use the same 1.5x formula as war magic, instead of 2.0x?
                if (criticalHit)
                    damageBonus = lifeMagicDamage * 0.5f * GetWeaponCritDamageMod(sourceCreature, attackSkill, target);

                finalDamage = (lifeMagicDamage + damageBonus) * elementalDmgBonus * slayerBonus * shieldMod;
                return finalDamage;
            }
            // war/void magic projectiles
            else
            {
                if (criticalHit)
                {
                    if (isPVP) // PvP: 50% of the MIN damage added to normal damage roll
                        damageBonus = Spell.MinDamage * 0.5f;
                    else   // PvE: 50% of the MAX damage added to normal damage roll
                        damageBonus = Spell.MaxDamage * 0.5f;

                    var critDamageMod = GetWeaponCritDamageMod(sourceCreature, attackSkill, target);

                    damageBonus *= critDamageMod;
                }

                /* War Magic skill-based damage bonus
                 * http://acpedia.org/wiki/Announcements_-_2002/08_-_Atonement#Letter_to_the_Players
                 */
                if (sourcePlayer != null)
                {
                    // per retail stats, level 8 difficulty is capped to 350 instead of 400
                    // without this, level 7s have the potential to deal more damage than level 8s
                    var difficulty = Math.Min(Spell.Power, 350);
                    var magicSkill = sourcePlayer.GetCreatureSkill(Spell.School).Current;

                    if (magicSkill > difficulty)
                    {
                        // Bonus clamped to a maximum of 50%
                        //var percentageBonus = Math.Clamp((magicSkill - Spell.Power) / 100.0f, 0.0f, 0.5f);
                        var percentageBonus = (magicSkill - difficulty) / 1000.0f;

                        warSkillBonus = Spell.MinDamage * percentageBonus;
                    }
                }
                var baseDamage = ThreadSafeRandom.Next(Spell.MinDamage, Spell.MaxDamage);

                var weaponResistanceMod = GetWeaponResistanceModifier(sourceCreature, attackSkill, Spell.DamageType);

                finalDamage = baseDamage + damageBonus + warSkillBonus;
                finalDamage *= target.GetResistanceMod(resistanceType, null, weaponResistanceMod)
                    * elementalDmgBonus * slayerBonus * shieldMod;

                return finalDamage;
            }
        }

        /// <summary>
        /// Calculates the amount of damage a shield absorbs from magic projectile
        /// </summary>
        public float GetShieldMod(Creature target)
        {
            // ensure combat stance
            if (target.CombatMode == CombatMode.NonCombat)
                return 1.0f;

            // does the player have a shield equipped?
            var shield = target.GetEquippedShield();
            if (shield == null || shield.GetProperty(PropertyFloat.AbsorbMagicDamage) == null) return 1.0f;

            // is spell projectile in front of player,
            // within shield effectiveness area?
            var effectiveAngle = 180.0f;
            var angle = target.GetAngle(this);
            if (Math.Abs(angle) > effectiveAngle / 2.0f)
                return 1.0f;

            // https://asheron.fandom.com/wiki/Shield
            // The formula to determine magic absorption for shields is:
            // Reduction Percent = (cap * specMod * baseSkill * 0.003f) - (cap * specMod * 0.3f)
            // Cap = Maximum reduction
            // SpecMod = 1.0 for spec, 0.8 for trained
            // BaseSkill = 100 to 433 (above 433 base shield you always achieve the maximum %)

            var shieldSkill = target.GetCreatureSkill(Skill.Shield);
            // ensure trained?
            if (shieldSkill.AdvancementClass < SkillAdvancementClass.Trained || shieldSkill.Base < 100)
                return 1.0f;

            var baseSkill = Math.Min(shieldSkill.Base, 433);
            var specMod = shieldSkill.AdvancementClass == SkillAdvancementClass.Specialized ? 1.0f : 0.8f;
            var cap = (float)(shield.GetProperty(PropertyFloat.AbsorbMagicDamage) ?? 0.0f);

            // speced, 100 skill = 0%
            // trained, 100 skill = 0%
            // speced, 200 skill = 30%
            // trained, 200 skill = 24%
            // speced, 300 skill = 60%
            // trained, 300 skill = 48%
            // speced, 433 skill = 100%
            // trained, 433 skill = 80%

            var reduction = (cap * specMod * baseSkill * 0.003f) - (cap * specMod * 0.3f);

            var shieldMod = 1.0f - reduction;
            return shieldMod;
        }

        /// <summary>
        /// Called for a spell projectile to damage its target
        /// </summary>
        public void DamageTarget(WorldObject _target, double? damage, bool critical, bool critDefended = false)
        {
            var player = ProjectileSource as Player;

            var target = _target as Creature;
            var targetPlayer = _target as Player;

            if (targetPlayer != null && (targetPlayer.Invincible || targetPlayer.IsDead))
                return;

            uint amount;
            var percent = 0.0f;
            var heritageMod = 1.0f;
            var sneakAttackMod = 1.0f;

            // handle life projectiles for stamina / mana
            if (Spell.Category == SpellCategory.StaminaLowering)
            {
                percent = (float)damage / target.Stamina.MaxValue;
                amount = (uint)-target.UpdateVitalDelta(target.Stamina, (int)-Math.Round(damage.Value));
            }
            else if (Spell.Category == SpellCategory.ManaLowering)
            {
                percent = (float)damage / target.Mana.MaxValue;
                amount = (uint)-target.UpdateVitalDelta(target.Mana, (int)-Math.Round(damage.Value));
            }
            else
            {
                // for possibly applying sneak attack to magic projectiles,
                // only do this for health-damaging projectiles?
                if (player != null)
                {
                    // TODO: use target direction vs. projectile position, instead of player position
                    // could sneak attack be applied to void DoTs?
                    sneakAttackMod = player.GetSneakAttackMod(target);
                    //Console.WriteLine("Magic sneak attack:  + sneakAttackMod);
                    heritageMod = player.GetHeritageBonus(player.GetEquippedWand()) ? 1.05f : 1.0f;
                }

                // DR / DRR applies for magic too?
                var creatureSource = ProjectileSource as Creature;
                var damageRating = creatureSource != null ? creatureSource.GetDamageRating() : 0;
                var damageRatingMod = Creature.AdditiveCombine(Creature.GetPositiveRatingMod(damageRating), heritageMod, sneakAttackMod);
                var damageResistRatingMod = Creature.GetNegativeRatingMod(target.GetDamageResistRating(CombatType.Magic));
                damage *= damageRatingMod * damageResistRatingMod;

                //Console.WriteLine($"Damage rating: " + Creature.ModToRating(damageRatingMod));

                percent = (float)damage / target.Health.MaxValue;
                amount = (uint)-target.UpdateVitalDelta(target.Health, (int)-Math.Round(damage.Value));
                target.DamageHistory.Add(ProjectileSource, Spell.DamageType, amount);

                //if (targetPlayer != null && targetPlayer.Fellowship != null)
                    //targetPlayer.Fellowship.OnVitalUpdate(targetPlayer);
            }

            amount = (uint)Math.Round(damage.Value);    // full amount for debugging

            if (critical)
                target.EmoteManager.OnReceiveCritical(player);

            if (target.IsAlive)
            {
                string verb = null, plural = null;
                Strings.GetAttackVerb(Spell.DamageType, percent, ref verb, ref plural);
                var type = Spell.DamageType.GetName().ToLower();

                var critMsg = critical ? "Critical hit! " : "";
                var sneakMsg = sneakAttackMod > 1.0f ? "Sneak Attack! " : "";
                if (player != null)
                {
                    var critProt = critDefended ? " Your target's Critical Protection augmentation allows them to avoid your critical hit!" : "";

                    var attackerMsg = $"{critMsg}{sneakMsg}You {verb} {target.Name} for {amount} points with {Spell.Name}.{critProt}";

                    // could these crit / sneak attack?
                    if (Spell.Category == SpellCategory.StaminaLowering || Spell.Category == SpellCategory.ManaLowering)
                    {
                        var vital = Spell.Category == SpellCategory.StaminaLowering ? "stamina" : "mana";
                        attackerMsg = $"With {Spell.Name} you drain {amount} points of {vital} from {target.Name}.";
                    }

                    if (!player.SquelchManager.Squelches.Contains(target, ChatMessageType.Magic))
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat(attackerMsg, ChatMessageType.Magic));

                    player.Session.Network.EnqueueSend(new GameEventUpdateHealth(player.Session, target.Guid.Full, (float)target.Health.Current / target.Health.MaxValue));
                }

                if (targetPlayer != null)
                {
                    var critProt = critDefended ? " Your Critical Protection augmentation allows you to avoid a critical hit!" : "";

                    var defenderMsg = $"{critMsg}{sneakMsg}{ProjectileSource.Name} {plural} you for {amount} points with {Spell.Name}.{critProt}";

                    if (Spell.Category == SpellCategory.StaminaLowering || Spell.Category == SpellCategory.ManaLowering)
                    {
                        var vital = Spell.Category == SpellCategory.StaminaLowering ? "stamina" : "mana";
                        defenderMsg = $"{ProjectileSource.Name} casts {Spell.Name} and drains {amount} points of your {vital}.";
                    }

                    if (!targetPlayer.SquelchManager.Squelches.Contains(ProjectileSource, ChatMessageType.Magic))
                        targetPlayer.Session.Network.EnqueueSend(new GameMessageSystemChat(defenderMsg, ChatMessageType.Magic));
                }
            }
            else
            {
                target.OnDeath(ProjectileSource, Spell.DamageType, critical);
                target.Die();
            }
        }

        /// <summary>
        /// Sets the physics state for a launched projectile
        /// </summary>
        public void SetProjectilePhysicsState(WorldObject target, bool useGravity)
        {
            if (useGravity) GravityStatus = true;

            CurrentMotionState = null;
            Placement = null;

            // TODO: Physics description timestamps (sequence numbers) don't seem to be getting updated

            //Console.WriteLine("SpellProjectile PhysicsState: " + PhysicsObj.State);

            var pos = Location.Pos;
            var rotation = Location.Rotation;
            PhysicsObj.Position.Frame.Origin = pos;
            PhysicsObj.Position.Frame.Orientation = rotation;

            var velocity = Velocity;
            //velocity = Vector3.Transform(velocity, Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion(rotation)));
            PhysicsObj.Velocity = velocity.Value;
            if (target != null)
                PhysicsObj.ProjectileTarget = target.PhysicsObj;

            PhysicsObj.set_active(true);
        }
    }
}

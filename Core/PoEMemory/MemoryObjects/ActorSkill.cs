using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Serialization;
using ExileCore.Shared.Cache;
using ExileCore.Shared.Enums;
using GameOffsets;

namespace ExileCore.PoEMemory.MemoryObjects
{
    public class ActorSkill : RemoteMemoryObject
    {
	    public ActorSkill()
	    {
		    _skillUiState = new AreaCache<long>(GetSkillUiStatePtr);
		}

        public ushort Id => M.Read<ushort>(Address + 0x10);
        public GrantedEffectsPerLevel EffectsPerLevel => ReadObject<GrantedEffectsPerLevel>(Address + 0x18);
        public bool CanBeUsedWithWeapon => M.Read<byte>(Address + 0x50) > 0; 
        public bool CanBeUsed => M.Read<byte>(Address + 0x51) == 0;
        public int Cost => M.Read<byte>(Address + 0x54); 
        public int TotalUses => M.Read<int>(Address + 0x58);
        public float Cooldown => M.Read<int>(Address + 0x60) / 100f; //Converted milliseconds to seconds 
        public int SoulsPerUse => M.Read<int>(Address + 0x70);
        public int TotalVaalUses => M.Read<int>(Address + 0x74);
        public bool IsOnSkillBar => SkillSlotIndex != -1;
        public int SkillSlotIndex => TheGame.IngameState.ServerData.SkillBarIds.IndexOf(Id);

        public string Name
        {
            get
            {
                var id = Id;
                var effects = EffectsPerLevel;

                if (effects != null)
                {
                    var skill = effects.SkillGemWrapper;
                    var name = skill.Name;

                    if (string.IsNullOrEmpty(name))
                    {
                        name = skill.ActiveSkill.InternalName;

                        if (string.IsNullOrEmpty(name))
                            name = Id.ToString(CultureInfo.InvariantCulture);
                    }

                    return name;
                }
                else
                {
                    string name;

                    switch (id)
                    {
                        case 0x266:
                            name = "Interaction";
                            break;

                        case 0x2909:
                            name = "Move";
                            break;

                        default:
                            if (id != 0x37d9)
                                name = InternalName;
                            else
                                name = "WashedUp";

                            break;
                    }

                    return name;
                }
            }
        }

        internal int SlotIdentifier => (Id >> 8) & 0xff;
        public int SocketIndex => (SlotIdentifier >> 2) & 15;
        public bool IsUserSkill => (SlotIdentifier & 0x80) > 0;
        public bool AllowedToCast => CanBeUsedWithWeapon && CanBeUsed;
        public byte SkillUseStage => M.Read<byte>(Address + 0x8);
        public bool IsUsing => SkillUseStage > 2; //GetStat(PlayerStats.CastingSpell) > 0;
        public bool PrepareForUsage => SkillUseStage == 1; //GetStat(PlayerStats.CastingSpell) > 0;
        public TimeSpan CastTime => TimeSpan.FromMilliseconds((int) Math.Ceiling(1000f / (HundredTimesAttacksPerSecond / 100f)));
        public float Dps => GetStat(GameStat.HundredTimesDamagePerSecond + (IsUsing ? 4 : 0)) / 100f;
        public int HundredTimesAttacksPerSecond =>
            GetStat(IsUsing ? GameStat.HundredTimesCastsPerSecond : GameStat.HundredTimesAttacksPerSecond);
        public bool IsTotem => GetStat(GameStat.IsTotem) == 1 || GetStat(GameStat.SkillIsTotemified) == 1;
        public bool IsTrap => GetStat(GameStat.IsTrap) == 1 || GetStat(GameStat.SkillIsTrapped) == 1;
        public bool IsVaalSkill => SoulsPerUse >= 1 && TotalVaalUses >= 1;

        private bool IsMine => true; //TODO

        private readonly CachedValue<long> _skillUiState;

        public bool IsOnCooldown
        {
	        get
	        {
		        var ptr = _skillUiState.Value;
                if (ptr == long.MaxValue)
			        return false;
		        var state = M.Read<SkillUiStateOffsets>(ptr);
		        return (state.CooldownHigh - state.CooldownLow) >> 4 >= state.NumberOfUses;
	        }
        }
        public int RemainingUses
        {
	        get
	        {
		        var ptr = _skillUiState.Value;
		        if (ptr == long.MaxValue)
					return 0;
		        var state = M.Read<SkillUiStateOffsets>(ptr);
		        return state.NumberOfUses - ((int)(state.CooldownHigh - state.CooldownLow) >> 4);
	        }
        }

        long GetSkillUiStatePtr()
        {
	        var listStart = M.Read<long>(pTheGame.IngameState.ServerData.Address + 0x8088, 0x610, 0x28, 0x170);
	        var listEnd = M.Read<long>(pTheGame.IngameState.ServerData.Address + 0x8088, 0x610, 0x28, 0x178);
	        int maxCount = 100;
	        for (var ptr = listStart; ptr < listEnd && maxCount > 0; ptr += 0x48, maxCount--)
	        {
		        var state = M.Read<SkillUiStateOffsets>(ptr);
		        if (state.SkillId == Id)
			        return ptr;
	        }
	        return long.MaxValue;
        }


        public string InternalName
        {
            get
            {
                var effects = EffectsPerLevel;

                if (effects != null)
                    return effects.SkillGemWrapper.ActiveSkill.InternalName;

                switch (Id)
                {
                    case 0x266:
                        return "Interaction";
                    case 0x2909:
                        return "Move";
                    default:
                        if (Id != 0x37d9)
                            return Id.ToString(CultureInfo.InvariantCulture);
                        else
                            return "WashedUp";
                }
            }
        }

        public Dictionary<GameStat, int> Stats
        {
            get
            {
                var statsPtr = M.Read<long>(Address + 0xA0);
                var result = new Dictionary<GameStat, int>();

                ReadStats(result, statsPtr);

                //var additionalStatsPtr = M.Read<long>(Address + 0x60);
                //ReadStats(result, additionalStatsPtr);
                //And one more additional pointer in 0x10 -> 0x78 that I don't want to read
                return result;
            }
        }

        internal void ReadStats(Dictionary<GameStat, int> stats, long address)
        {
            var statPtrStart = M.Read<long>(address + 0xE8);
            var statPtrEnd = M.Read<long>(address + 0xF0);

            var key = 0;
            var value = 0;
            var total_stats = (int) (statPtrEnd - statPtrStart);
            var bytes = M.ReadMem(statPtrStart, total_stats);

            for (var i = 0; i < bytes.Length; i += 8)
            {
                try
                {
                    key = BitConverter.ToInt32(bytes, i);
                    value = BitConverter.ToInt32(bytes, i + 0x04);
                } 
                catch (Exception e)
                {
                    DebugWindow.LogError($"ActorSkill.ReadStats -> BitConverter failed, i: {i}");
                    DebugWindow.LogError($"ActorSkill.ReadStats -> {e.Message}");
                    continue;
                }

                stats[(GameStat) key] = value;
            }
        }

        public int GetStat(GameStat stat)
        {
            if (!Stats.TryGetValue(stat, out var num)) return 0;
            return num;
        }

        public override string ToString()
        {
            return $"IsUsing: {IsUsing}, {Name}, Id: {Id}, InternalName: {InternalName}, CanBeUsed: {CanBeUsed}";
        }
    }
}

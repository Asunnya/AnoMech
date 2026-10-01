using System;
using Lumina.Excel.Sheets;

namespace AnoMech.Scenarios.Dsr;

// NpcSpawn packet bodies for the demihuman (ModelChara Type 2) knights, which the hand-rolled
// spawn leaves T-posed. Header and common flags copy UMAD's real Graven Image capture; model,
// gear and HP come from the knight's BNpcBase and NpcEquip rows.
internal static unsafe class DsrNpcSpawn
{
    private const int PacketSize = 656;
    private const int Common = 0x10;
    private const uint NoTarget = 0xE0000000;
    private const uint DirectorEventId = 0x8003759A;

    public static byte[]? Build(uint baseId, uint nameId, uint maxHp)
    {
        if (!Plugin.DataManager.GetExcelSheet<BNpcBase>().TryGetRow(baseId, out var bnpc)) return null;
        var bytes = new byte[PacketSize];
        fixed (byte* packet = bytes)
        {
            packet[0x08] = 3;
            packet[0x0A] = 1;
            packet[0x0D] = 0x14;
            var c = packet + Common;
            *(ulong*)(c + 0x00) = NoTarget;
            *(uint*)(c + 0x30) = baseId;
            *(uint*)(c + 0x34) = nameId;
            *(uint*)(c + 0x40) = DirectorEventId;
            *(uint*)(c + 0x44) = NoTarget;
            *(uint*)(c + 0x48) = NoTarget;
            *(uint*)(c + 0x4C) = maxHp;
            *(uint*)(c + 0x50) = maxHp;
            *(uint*)(c + 0x54) = 0x40008;
            *(ushort*)(c + 0x5A) = 10000;
            *(ushort*)(c + 0x5C) = 10000;
            *(ushort*)(c + 0x60) = (ushort)bnpc.ModelChara.RowId;
            c[0x6F] = 1;
            c[0x71] = 2;
            c[0x72] = 5;
            c[0x75] = 4;
            c[0x76] = DsrConstants.Level;
            if (bnpc.NpcEquip.ValueNullable is { } equip) WriteGear(c, equip);
        }
        return bytes;
    }

    private static void WriteGear(byte* c, NpcEquip equip)
    {
        *(ulong*)(c + 0x10) = Weapon(equip.ModelMainHand, equip.DyeMainHand.RowId);
        *(ulong*)(c + 0x18) = Weapon(equip.ModelOffHand, equip.DyeOffHand.RowId);
        uint[] models = [equip.ModelHead, equip.ModelBody, equip.ModelHands, equip.ModelLegs, equip.ModelFeet,
            equip.ModelEars, equip.ModelNeck, equip.ModelWrists, equip.ModelRightRing, equip.ModelLeftRing];
        uint[] dyes = [equip.DyeHead.RowId, equip.DyeBody.RowId, equip.DyeHands.RowId, equip.DyeLegs.RowId, equip.DyeFeet.RowId,
            equip.DyeEars.RowId, equip.DyeNeck.RowId, equip.DyeWrists.RowId, equip.DyeRightRing.RowId, equip.DyeLeftRing.RowId];
        var slots = (uint*)(c + 0x1FC);
        for (var i = 0; i < models.Length; i++)
            slots[i] = (models[i] & 0x00FFFFFF) | (Math.Min(dyes[i], 0xFFu) << 24);
    }

    private static ulong Weapon(ulong model, uint dye) => (model & 0x0000FFFFFFFFFFFF) | ((ulong)Math.Min(dye, 0xFFu) << 48);
}

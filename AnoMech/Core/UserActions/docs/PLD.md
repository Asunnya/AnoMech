# Paladin (PLD)

_✅ works · ⚠️ partial · ❌ not simulated · ➖ nothing to simulate (pure damage/heal/mitigation, or a native game button-swap)._

## Actions
| Action | ID | Status | Note |
|--------|----|:------:|------|
| Fast Blade | 9 | ➖ | Combo advancement is generic. |
| Fight or Flight | 20 | ✅ | Fight or Flight (76) + Goring Blade Ready (3847). |
| Riot Blade | 15 | ➖ | Combo; MP out of scope. |
| Total Eclipse | 7381 | ➖ | AoE combo starter. |
| Shield Bash | 16 | ➖ | Stun on target. |
| Iron Will | 28 | ➖ | Tank stance toggle, non-rotational. |
| Release Iron Will | 32065 | ➖ | Stance toggle. |
| Shield Lob | 24 | ➖ | Ranged enmity. |
| Rage of Halone | 21 | ➖ | Combo finisher; no self-status. |
| Spirits Within | 29 | ➖ | MP restore. |
| Sheltron | 3542 | ✅ | Spends 50 Oath (generic cost pass). |
| Sentinel | 17 | ➖ | Mitigation. |
| Prominence | 16457 | ✅ | Divine Might (2673), Combo-gated on Total Eclipse→Prominence. |
| Cover | 27 | ✅ | Spends 50 Oath (generic cost pass). |
| Circle of Scorn | 23 | ➖ | AoE + DoT on enemies. |
| Hallowed Ground | 30 | ➖ | Invuln. |
| Bulwark | 22 | ➖ | Mitigation. |
| Goring Blade | 3538 | ✅ | Consumes Goring Blade Ready (3847). |
| Divine Veil | 3540 | ➖ | Party barrier + heal. |
| Clemency | 3541 | ➖ | Heal. |
| Royal Authority | 3539 | ✅ | Atonement Ready (1902) + Divine Might (2673), Combo-gated on Riot Blade→Royal Authority. |
| Intervention | 7382 | ✅ | Spends 50 Oath (generic cost pass). |
| Holy Spirit | 7384 | ⚠️ | Consumes Divine Might and a Requiescat stack in one cast; should consume Divine Might alone. |
| Intervene | 16461 | ➖ | Gap closer, native charges. |
| Requiescat | 7383 | ✅ | Requiescat (1368, 4 stk) + Confiteor Ready (3019). |
| Passage of Arms | 7385 | ➖ | Mitigation cone. |
| Holy Circle | 16458 | ⚠️ | Same double-consume as Holy Spirit. |
| Atonement | 16460 | ✅ | Consumes Atonement Ready (1902) + grants Supplication Ready (3827). |
| Supplication | 36918 | ✅ | Consumes Supplication Ready (3827) + grants Sepulchre Ready (3828). |
| Sepulchre | 36919 | ✅ | Consumes Sepulchre Ready (3828); ends chain. |
| Confiteor | 16459 | ✅ | Consumes Confiteor Ready (3019) + Requiescat stack; sets route step 1. |
| Holy Sheltron | 25746 | ✅ | Spends 50 Oath (generic cost pass). |
| Expiacion | 25747 | ➖ | AoE + MP. |
| Blade of Faith | 25748 | ✅ | Consumes Requiescat stack; route step 2. |
| Blade of Truth | 25749 | ✅ | Consumes Requiescat stack; route step 3. |
| Blade of Valor | 25750 | ✅ | Blade of Honor Ready (3831) + consumes Requiescat stack; resets route. |
| Guardian | 36920 | ➖ | Mitigation. |
| Imperator | 36921 | ✅ | Requiescat upgrade: Requiescat (1368, 4 stk) + Confiteor Ready (3019). |
| Blade of Honor | 36922 | ✅ | Consumes Blade of Honor Ready (3831). |

## Traits
Oath Mastery: +5 Oath per landed auto-attack (`JobActions.AutoAttacks`); Oath is seeded to 100 at scenario start (`JobActions.StartingResources`). Otherwise no trait gaps — the enhancement traits (Enhanced Fight or Flight, Divine Magic Mastery, Enhanced Prominence, Sword Oath, Enhanced Requiescat, Enhanced Blade of Valor) fold into their action rows; the rest are potency or native upgrades (➖).

## Role actions
Rampart, Low Blow, Provoke, Interject, Reprisal, Arm's Length, Shirk — all ➖.

## Not simulated
- **Holy Spirit / Holy Circle double-consume.** Each removes Divine Might (2673) and decrements a Requiescat (1368) stack in the same cast; in-game Divine Might is prioritized and spent alone, so a stack can be burned one cast early.
- **Confiteor route step values unconfirmed in-game.** The route is backed by the `PldConfiteorStep` gauge (Confiteor +1, Blade of Faith +1, Blade of Truth +1, Blade of Valor −3, Max 3) plus a 30s decay reset, but whether these exact step values drive `GetAdjustedActionId` (Confiteor→Faith→Truth→Valor swap) is not verified in-game.

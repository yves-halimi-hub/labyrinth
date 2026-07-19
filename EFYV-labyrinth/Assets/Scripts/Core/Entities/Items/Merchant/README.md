# Merchant Purchases

[Up to entity items](../README.md)

[`Purchasables.cs`](Purchasables.cs) defines the purchase contract and three implementations:

- `HealingPurchase` rejects a full-health target and otherwise heals with clamping.
- `TemporaryBuffPurchase` applies a real timed buff through `PlayerController.ApplyTimedBuff` (registered on the player, ticked centrally, reverted on expiry); unknown buff ids fail so the merchant refunds.
- `WeaponUpgradePurchase` asks `UpgradeManager` for a selection when the player has a weapon system.

`Apply` returns whether the purchase succeeded so `BaseMerchantProp` can either remove the item or refund its cost.

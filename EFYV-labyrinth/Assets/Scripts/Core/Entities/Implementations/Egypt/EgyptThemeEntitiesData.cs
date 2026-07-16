using UnityEngine;
using EFYV.Core.Data;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Data.Entities
{
    // MONSTERS
    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.MonsterEvilEye)]
    public class EvilEyeData : EnemyData { }

    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.MonsterEyeBearer)]
    public class EyeBearerData : EnemyData { }

    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.MonsterSphinxKitten)]
    public class SphinxKittenData : EnemyData { }

    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.MonsterSphinxCat)]
    public class SphinxCatData : EnemyData { }

    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.MonsterBabyMummies)]
    public class BabyMummiesData : EnemyData { }

    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.MonsterFemaleMummy)]
    public class FemaleMummyData : EnemyData { }

    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.MonsterMaleMummy)]
    public class MaleMummyData : EnemyData { }

    // MINIBOSSES
    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.MiniBossTut)]
    public class TutData : BossData { }

    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.MiniBossAnkhesenpaaten)]
    public class AnkhesenpaatenData : BossData { }

    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.MiniBossEyeOfProvidenceFake)]
    public class EyeOfProvidenceFakeData : BossData { }

    // BOSSES
    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.BossEyeOfProvidenceReal)]
    public class EyeOfProvidenceRealData : BossData { }

    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.BossPharaohAkhenaten)]
    public class PharaohAkhenatenData : BossData { }

    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.BossNefertiti)]
    public class NefertitiData : BossData { }

    // SPECIAL BLOCKING OBJECTS (Inherit from GameAssetData for Props)
    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.ObjectPyramids)]
    public class PyramidsData : GameAssetData { }

    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.ObjectCactus)]
    public class CactusData : GameAssetData { }

    // SPECIAL INTERACTABLE OBJECTS
    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.InteractablePyramidDoor)]
    public class PyramidDoorData : GameAssetData { }

    [DesignableAsset(GameConfig.DataConfig.SpecificEntities.InteractableClosedSarcophage)]
    public class ClosedSarcophageData : GameAssetData { }
}

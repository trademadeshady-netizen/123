namespace HerbBot.Interfaces;

public interface IObjectManager
{
    LocalPlayer GetLocalPlayer();

    /// <summary>Все объекты, пригодные для фарма (травы нужных типов).</summary>
    IReadOnlyList<WoWGameObject> GetWoWGameObjectForFarm();
    WoWGameObject? GetWoWGameObjectByName(string name);
    WoWGameObject? GetWoWGameObjectByEntry(int id);

    /// <summary>Жив ли ещё объект в памяти/мире. Критично: трава может исчезнуть (её собрал другой игрок).</summary>
    bool IsValid(ulong guid);

    /// <summary>Есть ли другие игроки рядом с точкой (для IgnoreIfPlayerNear).</summary>
    bool IsOtherPlayerNear(Math.Vector3 point, float radius);
}

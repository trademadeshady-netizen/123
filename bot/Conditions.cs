using HerbBot.Interfaces;
namespace HerbBot.States;

public static class Conditions
{
    /// <summary>Нас атакуют и игнорировать это нельзя.</summary>
    public static bool IsAttackedAndCannotIgnore(LocalPlayer p) => p.InCombat;
}

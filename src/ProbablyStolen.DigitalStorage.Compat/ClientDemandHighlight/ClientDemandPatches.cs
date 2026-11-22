using System;
using System.Reflection;
using HarmonyLib;
using ProbablyStolen.DigitalStorage.Api.Integration;

namespace ProbablyStolen.DigitalStorage.Compat.ClientDemandHighlight;

internal static class ClientDemandPatches
{
    // Assembly-CSharp.dll from Steam build 26525552 (0.46D).
    private static readonly Guid SupportedModule = new("911508a6-e0eb-4cc0-91e8-e14b16ea28d7");

    internal static Action? Invalidate { private get; set; }

    internal static void Register(IIntegrationRegistration registration)
    {
        var gameModule = typeof(PlayerStore).Module;
        if (gameModule.ModuleVersionId != SupportedModule)
        {
            registration.Fail("native_game_build_mismatch");
            return;
        }

        var postfix = AccessTools.DeclaredMethod(typeof(ClientDemandPatches), nameof(AfterDemandChanged), Type.EmptyTypes);
        var clientPostfix = AccessTools.DeclaredMethod(typeof(ClientDemandPatches), nameof(AfterClientDemandChanged),
            new[] { typeof(StoreClient) });
        // The loader scanner requires a literal typeof receiver at each lookup.
        Add(AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.GetNextClient),
            Type.EmptyTypes), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.SetupWaitingClient),
            new[] { typeof(string) }), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.DismissCurrentClient),
            new[] { typeof(bool), typeof(bool), typeof(bool) }), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.Reset),
            Type.EmptyTypes), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(MapUIManager), nameof(MapUIManager.ActivateClientOutsideStore),
            Type.EmptyTypes), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(MapUIManager), nameof(MapUIManager.DeactivateClientOutsideStore),
            Type.EmptyTypes), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.SellItem),
            new[] { typeof(GameItem) }), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.OnItemBought),
            new[] { typeof(GameItem), typeof(int) }), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(StoreClient), nameof(StoreClient.OnDealAccepted),
            new[] { typeof(Dialogue) }), typeof(void), currentClientOnly: true);
        Add(AccessTools.DeclaredMethod(typeof(StoreClient), nameof(StoreClient.SetBarterOffer),
            new[] { typeof(BarterOffer) }), typeof(void), currentClientOnly: true);
        Add(AccessTools.DeclaredMethod(typeof(DialogUIManager), nameof(DialogUIManager.StartDialogue),
            new[] { typeof(Dialogue), typeof(bool) }), typeof(bool));
        Add(AccessTools.DeclaredMethod(typeof(DialogUIManager), nameof(DialogUIManager.ContinueConversation),
            Type.EmptyTypes), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(DialogUIManager), nameof(DialogUIManager.SelectChoice),
            new[] { typeof(int) }), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(Dialogue), nameof(Dialogue.ExecuteEndAction),
            Type.EmptyTypes), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(BargainUIManager), nameof(BargainUIManager.OfferMarkup),
            new[] { typeof(int) }), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(BargainUIManager), nameof(BargainUIManager.Blackmail),
            Type.EmptyTypes), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(BargainUIManager), nameof(BargainUIManager.DemandBuyingDiscount),
            new[] { typeof(int) }), typeof(void));
        Add(AccessTools.DeclaredMethod(typeof(BargainUIManager), nameof(BargainUIManager.ThreatenAug),
            Type.EmptyTypes), typeof(void));

        void Add(MethodInfo? target, Type returnType, bool currentClientOnly = false)
        {
            registration.Patch(target != null && target.Module == gameModule && target.IsPublic && !target.IsStatic &&
                !target.IsGenericMethod && target.ReturnType == returnType ? target : null,
                postfix: currentClientOnly ? clientPostfix : postfix);
        }
    }

    private static void AfterDemandChanged() => Invalidate?.Invoke();

    private static void AfterClientDemandChanged(StoreClient __instance)
    {
        var invalidate = Invalidate;
        if (invalidate != null && ReferenceEquals(__instance, PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint()))
            invalidate();
    }
}

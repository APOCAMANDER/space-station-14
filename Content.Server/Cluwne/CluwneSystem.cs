using Content.Server.Administration.Commands;
using Content.Server.Popups;
using Content.Shared.Popups;
using Content.Shared.Mobs;
using Content.Server.Chat;
using Content.Server.Chat.Systems;
using Content.Shared.Chat.Prototypes;
using Robust.Shared.Random;
using Content.Shared.Stunnable;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage;
using Robust.Shared.Prototypes;
using Content.Server.Emoting.Systems;
using Content.Server.Speech.EntitySystems;
using Content.Shared.Cluwne;
using Content.Shared.Interaction.Components;
using Content.Shared.Stealth.Components;
using Content.Server.Abilities.Mime;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Zombies;
using Content.Server.Mind.Components;
using Content.Server.StationEvents.Components;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking.Rules;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Content.Server.Cluwne;

public sealed class CluwneSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly SharedStunSystem _stunSystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly AutoEmoteSystem _autoEmote = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly IChatManager _chatMan = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CluwneComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<CluwneComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<CluwneComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<CluwneComponent, EmoteEvent>(OnEmote, before:
        new[] { typeof(VocalSystem), typeof(BodyEmotesSystem) });
    }

    /// <summary>
    /// On death removes active comps and gives genetic damage to prevent cloning, reduce this to allow cloning.
    /// </summary>
    private void OnMobState(EntityUid uid, CluwneComponent component, MobStateChangedEvent args)
    {

        if (args.NewMobState == MobState.Alive && component.IsBeast == true)
        {
            EnsureComp<StealthOnMoveComponent>(uid);
        }

        if (args.NewMobState == MobState.Critical && component.IsBeast == true)
        {
            RemComp<StealthOnMoveComponent>(uid);
        }

        if (args.NewMobState == MobState.Dead && component.IsBeast == true)
        {
            RemComp<StealthOnMoveComponent>(uid);
            RemComp<AutoEmoteComponent>(uid);
        }


        if (args.NewMobState == MobState.Dead && component.IsCluwne)
		{
            RemComp<CluwneComponent>(uid);
            RemComp<ClumsyComponent>(uid);
            RemComp<AutoEmoteComponent>(uid);
            var damageSpec = new DamageSpecifier(_prototypeManager.Index<DamageGroupPrototype>("Genetic"), 300);
            _damageableSystem.TryChangeDamage(uid, damageSpec);
        }
    }

    public EmoteSoundsPrototype? EmoteSounds;

    /// <summary>
    /// OnStartup gives the cluwne outfit, ensures clumsy, gives name prefix and makes sure emote sounds are laugh.
    /// </summary>
    private void OnComponentStartup(EntityUid uid, CluwneComponent component, ComponentStartup args)
    {
        if (component.EmoteSoundsId == null)
            return;
        _prototypeManager.TryIndex(component.EmoteSoundsId, out EmoteSounds);

        var meta = MetaData(uid);
        var name = meta.EntityName;

        EnsureComp<AutoEmoteComponent>(uid);
        _autoEmote.AddEmote(uid, component.AutoEmoteSound);
        EnsureComp<ClumsyComponent>(uid);

        if (component.IsCluwne)
        {
            _popupSystem.PopupEntity(Loc.GetString("cluwne-transform", ("target", uid)), uid, PopupType.LargeCaution);
            _audio.PlayPvs(component.SpawnSound, uid);
            meta.EntityName = Loc.GetString("cluwne-name-prefix", ("target", name));
            SetOutfitCommand.SetOutfit(uid, "CluwneGear", EntityManager);
        }
    }

    /// <summary>
    /// Handles the timing on autoemote as well as falling over and honking.
    /// </summary>
    private void OnEmote(EntityUid uid, CluwneComponent component, ref EmoteEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = _chat.TryPlayEmoteSound(uid, EmoteSounds, args.Emote);

        if (_robustRandom.Prob(component.GiggleRandomChance))
        {
            _audio.PlayPvs(component.SpawnSound, uid);
            _chat.TrySendInGameICMessage(uid, "honks", InGameICChatType.Emote, ChatTransmitRange.Normal);
        }

        else if (_robustRandom.Prob(component.KnockChance))
        {
            _audio.PlayPvs(component.KnockSound, uid);
            _stunSystem.TryParalyze(uid, TimeSpan.FromSeconds(component.ParalyzeTime), true);
            _chat.TrySendInGameICMessage(uid, "spasms", InGameICChatType.Emote, ChatTransmitRange.Normal);
        }
    }

    public CluwneBeastSpawnRuleComponent RuleConfig(EntityUid uid)
    {
        return Comp<CluwneBeastSpawnRuleComponent>(uid);
    }

    private void OnMeleeHit(EntityUid uid, CluwneComponent component, MeleeHitEvent args)
    {
        if (component.CluwneOnMelee == true)
        {

            foreach (var entity in args.HitEntities)
            {
                if (HasComp<HumanoidAppearanceComponent>(entity)
                    && !_mobStateSystem.IsDead(entity)
                    && _robustRandom.Prob(component.Cluwinification)
                    && !HasComp<ClumsyComponent>(entity)
                    && !HasComp<ZombieComponent>(entity)
                    && !HasComp<MimePowersComponent>(entity))
                {
                    _audio.PlayPvs(component.CluwneSound, uid);
                    EnsureComp<CluwneComponent>(entity);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ImmersiveInventory.Rules;

/// <summary> Physics rules from JSON assets of every domain plus rules registered in code. </summary>
internal sealed class RuleRegistry
{
    /// <summary> Path inside the <c>config</c> asset category. </summary>
    public const string AssetFolder = "immersiveinventory/rules/";

    private readonly List<PhysicsRule> fromAssets = new();
    private readonly List<PhysicsRule> fromCode = new();
    private readonly Dictionary<CollectibleObject, CompiledRule?> picked = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> disabled = new(StringComparer.OrdinalIgnoreCase);
    private CompiledRule[] compiled = Array.Empty<CompiledRule>();
    private System.Func<string, Type?>? classResolver;
    private ILogger? logger;
    private bool dirty = true;

    public int AssetCount => fromAssets.Count;

    public int CodeCount => fromCode.Count;

    /// <summary> The active rules in the order they are checked. </summary>
    public IReadOnlyList<CompiledRule> Rules
    {
        get
        {
            CompileIfNeeded();
            return compiled;
        }
    }

    /// <summary> <paramref name="resolveClass"/> turns a registered class name into its type, null if unknown. </summary>
    public void Attach(System.Func<string, Type?> resolveClass, ILogger log)
    {
        classResolver = resolveClass;
        logger = log;
        dirty = true;
    }

    public void Register(PhysicsRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        fromCode.Add(rule);
        dirty = true;
    }

    public void SetDisabled(IEnumerable<string> codes)
    {
        disabled.Clear();
        disabled.UnionWith(codes);
        dirty = true;
    }

    /// <summary> Replaces the rules loaded from assets. Broken files are logged and skipped. </summary>
    public void LoadAssets(IAssetManager assets, ILogger log)
    {
        fromAssets.Clear();
        dirty = true;

        foreach (var asset in assets.GetManyInCategory("config", AssetFolder))
        {
            if (!asset.Location.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                foreach (var rule in asset.ToObject<PhysicsRule[]>() ?? Array.Empty<PhysicsRule>())
                {
                    if (rule == null)
                    {
                        continue;
                    }

                    rule.Domain = asset.Location.Domain;
                    fromAssets.Add(rule);
                }
            }
            catch (Exception e)
            {
                log.Error("Failed to load physics rules {0}, skipping the file: {1}", asset.Location, e.Message);
            }
        }
    }

    /// <summary> The rule that decides for <paramref name="collectible"/>, or null if none matches. </summary>
    public CompiledRule? Pick(CollectibleObject collectible)
    {
        CompileIfNeeded();

        if (!picked.TryGetValue(collectible, out var rule))
        {
            rule = compiled.FirstOrDefault(candidate => candidate.Matches(collectible));
            picked[collectible] = rule;
        }

        return rule;
    }

    /// <summary> How <paramref name="collectible"/> swings on the cursor; null keeps it as in vanilla. </summary>
    public PhysicsProfile? Resolve(CollectibleObject collectible) => Pick(collectible)?.Profile;

    private void CompileIfNeeded()
    {
        if (!dirty)
        {
            return;
        }

        dirty = false;
        picked.Clear();
        compiled = fromAssets.Concat(fromCode)
            .Where(rule => !disabled.Contains(rule.Code ?? ""))
            .OrderByDescending(rule => rule.Priority)
            .Select(rule => new CompiledRule(rule, classResolver, logger))
            .Where(rule => rule.CanMatch)
            .ToArray();
    }
}

/// <summary> How the stacks of one rule swing. </summary>
public sealed class PhysicsProfile
{
    public PhysicsProfile(string ruleCode, PhysicsRule rule, AssetLocation? swing)
    {
        RuleCode = ruleCode;
        Gravity = Positive(rule.Gravity, 1f);
        Damping = Positive(rule.Damping, 1f);
        Inertia = Positive(rule.Inertia, 1f);
        Stiffness = Positive(rule.Stiffness, 0f);
        Swing = swing;
        SwingSpeed = rule.SwingSpeed is float speed && float.IsFinite(speed) && speed > 0f ? speed : null;
        Grip = rule.Grip is { Length: > 0 } grip ? grip : null;
    }

    public string RuleCode { get; }

    public float Gravity { get; }

    public float Damping { get; }

    public float Inertia { get; }

    public float Stiffness { get; }

    public AssetLocation? Swing { get; }

    public float? SwingSpeed { get; }

    public GripZone[]? Grip { get; }

    private static float Positive(float value, float fallback) => float.IsFinite(value) && value >= 0f ? value : fallback;
}

/// <summary> A rule with its conditions parsed. </summary>
public sealed class CompiledRule
{
    private readonly AssetLocation[] codes;
    private readonly Type[] classes;
    private readonly EnumTool[] tools;
    private readonly EnumItemClass? type;
    private readonly string[] drawTypes;

    internal CompiledRule(PhysicsRule rule, System.Func<string, Type?>? resolveClass, ILogger? logger)
    {
        Source = rule;
        Code = string.IsNullOrWhiteSpace(rule.Code) ? "(unnamed)" : rule.Code.Trim();
        var match = rule.Match;
        bool canMatch = true;

        codes = Clean(match?.Codes).Select(code => AssetLocation.Create(code)).ToArray();

        var classNames = Clean(match?.Classes);
        var resolvedClasses = new List<Type>();

        foreach (string className in classNames)
        {
            if (resolveClass?.Invoke(className) is { } resolved)
            {
                resolvedClasses.Add(resolved);
            }
            else
            {
                logger?.Warning("Physics rule {0}: unknown block or item class {1}.", Code, className);
            }
        }

        classes = resolvedClasses.ToArray();
        // A rule whose classes all failed to resolve must not fall back to matching everything.
        canMatch &= classNames.Length == 0 || classes.Length > 0;

        var toolNames = Clean(match?.Tools);
        var resolvedTools = new List<EnumTool>();

        foreach (string toolName in toolNames)
        {
            if (Enum.TryParse<EnumTool>(toolName, ignoreCase: true, out var tool))
            {
                resolvedTools.Add(tool);
            }
            else
            {
                logger?.Warning("Physics rule {0}: unknown tool type {1}.", Code, toolName);
            }
        }

        tools = resolvedTools.ToArray();
        canMatch &= toolNames.Length == 0 || tools.Length > 0;

        if (!string.IsNullOrWhiteSpace(match?.Type))
        {
            switch (match.Type.Trim().ToLowerInvariant())
            {
                case "item":
                    type = EnumItemClass.Item;
                    break;
                case "block":
                    type = EnumItemClass.Block;
                    break;
                default:
                    logger?.Warning("Physics rule {0}: type must be item or block, not {1}.", Code, match.Type);
                    canMatch = false;
                    break;
            }
        }

        Food = match?.Food;
        drawTypes = Clean(match?.DrawTypes);
        CanMatch = canMatch;

        if (rule.Physics)
        {
            string domain = string.IsNullOrEmpty(rule.Domain) ? ImmersiveInventoryModSystem.Domain : rule.Domain;
            var swing = string.IsNullOrWhiteSpace(rule.Swing) ? null : AssetLocation.Create(rule.Swing.Trim(), domain);
            Profile = new PhysicsProfile(Code, rule, swing);
        }
    }

    public PhysicsRule Source { get; }

    public string Code { get; }

    /// <summary> False for rules that would match the wrong things because some of their conditions could not be understood. </summary>
    public bool CanMatch { get; }

    /// <summary> Null when the rule turns physics off. </summary>
    public PhysicsProfile? Profile { get; }

    private bool? Food { get; }

    public bool Matches(CollectibleObject collectible)
    {
        var code = collectible.Code;

        if (code == null)
        {
            return false;
        }

        if (codes.Length > 0 && !codes.Any(pattern => WildcardUtil.Match(pattern, code)))
        {
            return false;
        }

        if (classes.Length > 0 && !classes.Any(candidate => candidate.IsInstanceOfType(collectible)))
        {
            return false;
        }

        if (tools.Length > 0 && !(collectible.Tool is EnumTool tool && Array.IndexOf(tools, tool) >= 0))
        {
            return false;
        }

        if (type is { } itemClass && collectible.ItemClass != itemClass)
        {
            return false;
        }

        if (Food is bool food && collectible.NutritionProps != null != food)
        {
            return false;
        }

        if (drawTypes.Length > 0 && !(collectible is Block block && drawTypes.Any(pattern => Glob(pattern, block.DrawType.ToString()))))
        {
            return false;
        }

        return true;
    }

    private static bool Glob(string pattern, string text) => pattern.EndsWith('*')
        ? text.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
        : string.Equals(pattern, text, StringComparison.OrdinalIgnoreCase);

    private static string[] Clean(string[]? values) => (values ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .ToArray();
}

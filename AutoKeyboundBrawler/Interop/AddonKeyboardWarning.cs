using AutoKeyboundBrawler.Data;
using ECommons;
using ECommons.Automation;
using ECommons.Automation.UIInput;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using AddonSelectYesnoStruct = FFXIVClientStructs.FFXIV.Client.UI.AddonSelectYesno;

namespace AutoKeyboundBrawler.Interop;

/// <summary>
/// Handles the game's own "A usable keyboard could not be found" warning.
/// This SelectYesno popup appears when the keyboard is disconnected or re-enumerated
/// (for example when switching a KVM), and answering "No" closes the game client.
/// The prompt text is localized through Addon sheet row 17826 so the match works in
/// every client language without hardcoded strings.
/// </summary>
public static unsafe class AddonKeyboardWarning
{
    public const string AddonName = "SelectYesno";

    private static string? _expectedPrompt;
    private static DateTime _lastClickUtc = DateTime.MinValue;

    /// <summary>Raw localized warning text from Addon sheet row 17826 (empty until loaded).</summary>
    public static string ExpectedPrompt
    {
        get
        {
            EnsureExpectedPrompt();
            return _expectedPrompt ?? string.Empty;
        }
    }

    public static bool TryGet(out AtkUnitBase* addon)
    {
        try
        {
            return GenericHelpers.TryGetAddonByName(AddonName, out addon) &&
                   addon != null &&
                   addon->IsVisible;
        }
        catch
        {
            addon = null;
            return false;
        }
    }

    /// <summary>
    /// True when the visible SelectYesno window is the keyboard detection warning.
    /// </summary>
    public static bool IsKeyboardWarningOpen()
    {
        if (!TryGet(out var addon)) return false;
        return IsKeyboardWarning(addon);
    }

    /// <summary>
    /// Accepts the keyboard warning with "Yes" when it is open.
    /// Returns true whenever the warning is present (even if the click could not be
    /// performed yet) so callers can suppress every other automation path.
    /// </summary>
    public static bool HandleIfOpen()
    {
        if (!TryGet(out var addon) || !IsKeyboardWarning(addon))
        {
            return false;
        }

        if (addon->IsReady)
        {
            TryClickYes(addon);
        }

        return true;
    }

    private static bool IsKeyboardWarning(AtkUnitBase* addon)
    {
        if (addon == null || !addon->IsVisible) return false;

        var expected = ExpectedPrompt;
        if (expected.Length == 0) return false;

        try
        {
            var yesno = (AddonSelectYesnoStruct*)addon;
            if (yesno->PromptText == null) return false;

            var prompt = GUINodeUtils.GetNodeText((AtkResNode*)yesno->PromptText);
            return KTGLocalizationRegistry.MatchesKeyboardWarningPrompt(prompt, expected);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryClickYes(AtkUnitBase* addon)
    {
        if (addon == null || !addon->IsReady) return false;

        // Avoid firing several clicks while the addon is closing.
        if ((DateTime.UtcNow - _lastClickUtc).TotalMilliseconds < 500) return false;

        try
        {
            var yesno = (AddonSelectYesnoStruct*)addon;
            var button = yesno->YesButton;
            if (button != null &&
                button->IsEnabled &&
                button->AtkComponentBase.OwnerNode != null)
            {
                button->ClickAddonButton(addon);
                addon->Update(0);
                _lastClickUtc = DateTime.UtcNow;
                Svc.Log.Information("[AddonKeyboardWarning] Accepted the game's keyboard warning via YesButton.");
                return true;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[AddonKeyboardWarning] Native YesButton click failed");
        }

        try
        {
            // Callback 0 is "Yes" on SelectYesno. Only reached when the prompt was
            // positively identified as the keyboard warning, so it can never answer "No".
            Callback.Fire(addon, true, 0);
            _lastClickUtc = DateTime.UtcNow;
            Svc.Log.Information("[AddonKeyboardWarning] Accepted the game's keyboard warning via Yes callback.");
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[AddonKeyboardWarning] Keyboard warning Yes callback failed");
        }

        return false;
    }

    private static void EnsureExpectedPrompt()
    {
        if (_expectedPrompt != null) return;

        try
        {
            var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Addon>();
            var text = sheet?.GetRowOrDefault(KTGLocalizationRegistry.KeyboardWarningAddonRowId)?.Text.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                _expectedPrompt = text;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[AddonKeyboardWarning] Could not read Addon sheet row 17826");
        }
    }
}

using Godot;
using Jabroni.Data;
using Jabroni.Inventory;

namespace Jabroni.UI.Inventory;

/// <summary>
/// The inventory's entry point: a button parked in the corner, and the full-screen panel it
/// opens. The panel holds an <see cref="InventoryGrid"/> and a details readout underneath it,
/// which follows the grid's selection.
/// <para>
/// The panel's backdrop deliberately covers the viewport and stops mouse input. ClickToMove
/// reads clicks in _UnhandledInput, so a Control that swallows them is what keeps a tap on the
/// grid from also ordering the avatar to walk to whatever is behind the panel.
/// </para>
/// </summary>
public partial class InventoryUI : Control
{
    private const string TitleKey = "UI.Inventory.Title";
    private const string CloseKey = "UI.Inventory.Close";
    private const string EmptyKey = "UI.Inventory.Empty";
    private const string SelectPromptKey = "UI.Inventory.SelectPrompt";
    private const string ValueKey = "UI.Inventory.Value";

    private static readonly Color PanelColor = new(0.09f, 0.10f, 0.12f, 0.97f);
    private static readonly Color PanelBorderColor = new(0.30f, 0.32f, 0.38f);
    private static readonly Color MutedTextColor = new(0.62f, 0.64f, 0.70f);
    private static readonly Color NameTextColor = new(0.96f, 0.94f, 0.88f);

    private Control _screen;
    private Button _openButton;
    private InventoryGrid _grid;
    private Label _itemName;
    private Label _itemDesc;
    private Label _itemValue;

    public override void _Ready()
    {
        _screen = GetNode<Control>("Screen");
        _openButton = GetNode<Button>("OpenButton");
        _grid = GetNode<InventoryGrid>("Screen/Center/Panel/Margin/Layout/Grid");
        _itemName = GetNode<Label>("Screen/Center/Panel/Margin/Layout/Details/ItemName");
        _itemDesc = GetNode<Label>("Screen/Center/Panel/Margin/Layout/Details/ItemDesc");
        _itemValue = GetNode<Label>("Screen/Center/Panel/Margin/Layout/Details/ItemValue");

        var closeButton = GetNode<Button>("Screen/Center/Panel/Margin/Layout/Header/CloseButton");
        var title = GetNode<Label>("Screen/Center/Panel/Margin/Layout/Header/Title");

        _openButton.Text = Tr(TitleKey);
        closeButton.Text = Tr(CloseKey);
        title.Text = Tr(TitleKey);

        _openButton.Pressed += Open;
        closeButton.Pressed += Close;
        _grid.SelectionChanged += ShowDetails;
        _screen.GuiInput += OnBackdropInput;

        StylePanel();
        StyleOpenButton();

        _itemName.AddThemeColorOverride("font_color", NameTextColor);
        _itemDesc.AddThemeColorOverride("font_color", MutedTextColor);
        _itemValue.AddThemeColorOverride("font_color", MutedTextColor);

        SetOpen(false);
        ShowDetails("");
    }

    // Escape closes the panel. Read as unhandled key input rather than _Input so the buttons and
    // the grid still get first refusal on anything they care about.
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_screen.Visible && @event.IsActionPressed("ui_cancel"))
        {
            Close();
            AcceptEvent();
        }
    }

    // Tapping the darkened area outside the panel closes it, which is how a phone player will
    // expect to dismiss a sheet like this. Only presses that miss the panel arrive here: Screen
    // stops mouse input, and the panel on top of it stops its own, so this fires for the backdrop
    // alone without needing to hit-test the panel's rect by hand.
    private void OnBackdropInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            Close();
            _screen.AcceptEvent();
        }
    }

    private void Open()
    {
        SetOpen(true);

        // Opening on a blank details panel, rather than on whatever was selected last time, so
        // the readout always describes something the player picked in this sitting.
        _grid.ClearSelection();
    }

    private void Close()
    {
        SetOpen(false);
    }

    // The open button goes away with the panel up rather than sitting greyed under the backdrop:
    // the backdrop already swallows presses aimed at it, so leaving it on screen would only
    // advertise a control that no longer does anything.
    private void SetOpen(bool open)
    {
        _screen.Visible = open;
        _openButton.Visible = !open;
    }

    private void ShowDetails(string itemId)
    {
        var inventory = GetNode<PlayerInventory>("/root/PlayerInventory");
        TsvRow row = inventory.ItemRow(itemId);

        if (row == null)
        {
            _itemName.Text = Tr(_grid.SlotCount == 0 ? EmptyKey : SelectPromptKey);
            _itemName.AddThemeColorOverride("font_color", MutedTextColor);
            _itemDesc.Text = "";
            _itemValue.Text = "";
            return;
        }

        _itemName.Text = Tr(row.GetString(ItemSchema.NameColumn));
        _itemName.AddThemeColorOverride("font_color", NameTextColor);
        _itemDesc.Text = Tr(row.GetString(ItemSchema.DescColumn));
        _itemValue.Text = string.Format(
            Tr(ValueKey),
            row.GetInt(ItemSchema.ValueColumn),
            row.GetInt(ItemSchema.SellValueColumn));
    }

    // The default theme's button is nearly transparent, which disappears against sunlit terrain
    // -- this gives the one control that sits over the 3D world an opaque chip to read against.
    private void StyleOpenButton()
    {
        var styleBox = new StyleBoxFlat
        {
            BgColor = PanelColor,
            BorderColor = PanelBorderColor,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ShadowColor = new Color(0f, 0f, 0f, 0.4f),
            ShadowSize = 6,
        };

        _openButton.AddThemeStyleboxOverride("normal", styleBox);
        _openButton.AddThemeStyleboxOverride("hover", styleBox);
        _openButton.AddThemeStyleboxOverride("pressed", styleBox);
        _openButton.AddThemeStyleboxOverride("focus", styleBox);
        _openButton.AddThemeColorOverride("font_color", NameTextColor);
        _openButton.AddThemeColorOverride("font_hover_color", NameTextColor);
        _openButton.AddThemeColorOverride("font_pressed_color", NameTextColor);
    }

    private void StylePanel()
    {
        var panel = GetNode<PanelContainer>("Screen/Center/Panel");
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = PanelColor,
            BorderColor = PanelBorderColor,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            ShadowColor = new Color(0f, 0f, 0f, 0.45f),
            ShadowSize = 12,
        });
    }
}

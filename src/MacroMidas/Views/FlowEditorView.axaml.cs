using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacroMidas.Models;
using MacroMidas.Native;
using MacroMidas.ViewModels;

namespace MacroMidas.Views;

public partial class FlowEditorView : UserControl
{
    private const double DragThreshold = 6;

    private FlowEditorViewModel? _vm;

    // Arrastar e soltar
    private FlowStep? _pressStep;
    private Point _pressPos;
    private bool _dragging;
    private Border? _dragCard;
    private (IList<FlowStep> List, int Index)? _dropTarget;

    public FlowEditorView()
    {
        InitializeComponent();
        StepsList.AddHandler(PointerPressedEvent, OnStepPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        StepsList.AddHandler(PointerMovedEvent, OnStepsPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        StepsList.AddHandler(PointerReleasedEvent, OnStepsPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        StepsList.AddHandler(PointerCaptureLostEvent, (_, _) => ResetDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
        RunPanel.CloseRequested += () => RunSettingsButton.Flyout?.Hide();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.BringIntoViewRequested -= OnBringIntoView;
            _vm = DataContext as FlowEditorViewModel;
            if (_vm != null) _vm.BringIntoViewRequested += OnBringIntoView;
        };
    }

    // Abre o painel de execução (repetir, onde roda, Lock, atalho), usado pelo menu Executar.
    public void OpenRunSettings() => RunSettingsButton.Flyout?.ShowAt(RunSettingsButton);

    private static Border? CardOf(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("stepCard"));

    private void OnStepPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        var card = CardOf(e.Source);
        if (card?.DataContext is FlowStep step)
        {
            _vm.SelectedStep = step;
            var onControl = (e.Source as Visual)?.GetSelfAndVisualAncestors().TakeWhile(v => v != card)
                .Any(v => v is Button or TextBox or ToggleSwitch) == true;
            if (!onControl && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _pressStep = step;
                _pressPos = e.GetPosition(StepsScroll);
            }
        }
        var onInteractive = (e.Source as Visual)?.GetSelfAndVisualAncestors()
            .Any(v => v is Button or TextBox or ToggleSwitch or NumericUpDown or Slider) == true;
        if (!onInteractive) Focus();
    }

    private void OnStepsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressStep == null || _vm == null) return;
        var pos = e.GetPosition(StepsScroll);
        if (!_dragging)
        {
            if (Math.Abs(pos.X - _pressPos.X) < DragThreshold && Math.Abs(pos.Y - _pressPos.Y) < DragThreshold) return;
            _dragging = true;
            _dragCard = FindCard(_pressStep);
            _dragCard?.Classes.Add("dragging");
            e.Pointer.Capture(StepsList);
        }
        UpdateDropTarget(e);
        AutoScroll(pos);
    }

    private void OnStepsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && _pressStep != null && _dropTarget is { } t && _vm != null)
        {
            var step = _pressStep;
            ResetDrag();
            e.Pointer.Capture(null);
            _vm.MoveStepTo(step, t.List, t.Index);
            return;
        }
        if (_dragging) e.Pointer.Capture(null);
        ResetDrag();
    }

    private void ResetDrag()
    {
        _dragCard?.Classes.Remove("dragging");
        _dragCard = null;
        _dragging = false;
        _pressStep = null;
        _dropTarget = null;
        DropIndicator.IsVisible = false;
    }

    private Border? FindCard(FlowStep step) =>
        StepsList.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("stepCard") && ReferenceEquals(b.DataContext, step));

    private bool IsInsideDragged(FlowStep step) =>
        _pressStep != null && Flow.Walk([_pressStep]).Contains(step);

    private bool ListInsideDragged(IList<FlowStep> list) =>
        _pressStep != null && Flow.Walk([_pressStep]).Any(s => s.GetChildLists().Any(l => ReferenceEquals(l, list)));

    private void UpdateDropTarget(PointerEventArgs e)
    {
        if (_vm == null) return;
        _dropTarget = null;
        var p = e.GetPosition(StepsList);
        var hit = StepsList.InputHitTest(p) as Visual;

        var add = hit?.GetSelfAndVisualAncestors().OfType<AddStepButton>().FirstOrDefault();
        if (add?.TargetList is { } addList && !ListInsideDragged(addList))
        {
            _dropTarget = (addList, addList.Count);
            ShowIndicator(add, before: true);
            return;
        }

        var card = CardOf(hit);
        while (card?.DataContext is FlowStep s && IsInsideDragged(s))
            card = CardOf(card.GetVisualParent());

        if (card?.DataContext is FlowStep over && _vm.FindParent(over) is { } list)
        {
            var rel = e.GetPosition(card);
            var before = rel.Y < card.Bounds.Height / 2;
            if (!before && over.IsBlock)
            {
                var inner = over.GetChildLists().First();
                _dropTarget = (inner, 0);
                ShowIndicator(card, before: false, indent: 33);
                return;
            }
            var idx = list.IndexOf(over);
            _dropTarget = (list, before ? idx : idx + 1);
            ShowIndicator(card, before);
            return;
        }

        var block = hit?.GetSelfAndVisualAncestors().OfType<Panel>().FirstOrDefault(pn => pn.Classes.Contains("stepBlock"));
        if (block?.DataContext is FlowStep owner && !IsInsideDragged(owner) && _vm.FindParent(owner) is { } ownerList)
        {
            _dropTarget = (ownerList, ownerList.IndexOf(owner) + 1);
            ShowIndicator(block, before: false);
            return;
        }

        var last = _vm.Flow.Steps.LastOrDefault(s => !IsInsideDragged(s));
        if (last != null && FindCard(last) is { } lastCard && p.Y > lastCard.TranslatePoint(new Point(0, lastCard.Bounds.Height), StepsList)?.Y)
        {
            _dropTarget = (_vm.Flow.Steps, _vm.Flow.Steps.Count);
            ShowIndicator(lastCard, before: false);
            return;
        }
        DropIndicator.IsVisible = false;
    }

    private void ShowIndicator(Control anchor, bool before, double indent = 0)
    {
        var tl = anchor.TranslatePoint(new Point(0, 0), DragLayer);
        if (tl == null)
        {
            DropIndicator.IsVisible = false;
            return;
        }
        Canvas.SetLeft(DropIndicator, tl.Value.X + indent);
        Canvas.SetTop(DropIndicator, before ? tl.Value.Y - 2 : tl.Value.Y + anchor.Bounds.Height);
        DropIndicator.Width = Math.Max(40, (anchor is AddStepButton ? anchor.Bounds.Width + 120 : anchor.Bounds.Width) - indent);
        DropIndicator.IsVisible = true;
    }

    private void AutoScroll(Point posInScroll)
    {
        const double edge = 36, speed = 14;
        var offset = StepsScroll.Offset;
        if (posInScroll.Y < edge) StepsScroll.Offset = offset.WithY(Math.Max(0, offset.Y - speed));
        else if (posInScroll.Y > StepsScroll.Bounds.Height - edge) StepsScroll.Offset = offset.WithY(offset.Y + speed);
    }

    private void OnBringIntoView(FlowStep step)
    {
        Dispatcher.UIThread.Post(() => FindCard(step)?.BringIntoView(), DispatcherPriority.Background);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _vm == null) return;

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is TextBox || e.Source is TextBox) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.Delete when !ctrl:
                _vm.DeleteCommand.Execute(null);
                break;
            case Key.D when ctrl:
                _vm.DuplicateCommand.Execute(null);
                break;
            case Key.Up when ctrl:
                _vm.MoveUpCommand.Execute(null);
                break;
            case Key.Down when ctrl:
                _vm.MoveDownCommand.Execute(null);
                break;
            case Key.C when ctrl:
                _vm.CopyCommand.Execute(null);
                break;
            case Key.V when ctrl:
                _vm.PasteCommand.Execute(null);
                break;
            case Key.Z when ctrl && !shift:
                _vm.UndoCommand.Execute(null);
                break;
            case Key.Y when ctrl:
            case Key.Z when ctrl && shift:
                _vm.RedoCommand.Execute(null);
                break;
            case Key.Space:
                _vm.ToggleEnabledCommand.Execute(null);
                break;
            case Key.Escape:
                if (_dragging) ResetDrag();
                else _vm.SelectedStep = null;
                break;
            case Key.Up:
            case Key.Down:
                SelectNeighbor(e.Key == Key.Down ? 1 : -1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void SelectNeighbor(int delta)
    {
        if (_vm == null) return;
        var all = Flow.Walk(_vm.Flow.Steps).ToList();
        if (all.Count == 0) return;
        var idx = _vm.SelectedStep == null ? (delta > 0 ? -1 : all.Count) : all.IndexOf(_vm.SelectedStep);
        var next = Math.Clamp(idx + delta, 0, all.Count - 1);
        _vm.SelectCommand.Execute(all[next]);
    }
}

using Microsoft.UI.Xaml.Controls;

namespace Arboryn.UI.Controls;

public sealed partial class EmptyStateView : UserControl
{
    public EmptyStateView()
    {
        InitializeComponent();
    }

    public string Glyph
    {
        set => GlyphIcon.Glyph = value;
    }

    public string Title
    {
        set => TitleText.Text = value;
    }

    public string Message
    {
        set => MessageText.Text = value;
    }

    public string Milestone
    {
        set => MilestoneText.Text = value;
    }
}

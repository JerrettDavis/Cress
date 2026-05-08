using System.Windows.Forms;

ApplicationConfiguration.Initialize();
Application.Run(new MainForm());

internal sealed class MainForm : Form
{
    private readonly TextBox _nameInput;
    private readonly Button _continueButton;
    private readonly Label _greetingLabel;

    public MainForm()
    {
        Text = "Cress Flawright Test App";
        Width = 420;
        Height = 220;
        StartPosition = FormStartPosition.CenterScreen;

        _nameInput = new TextBox
        {
            Name = "NameInput",
            Left = 24,
            Top = 24,
            Width = 220,
            TabIndex = 0
        };

        var promptLabel = new Label
        {
            Name = "PromptLabel",
            Left = 24,
            Top = 4,
            Width = 120,
            Text = "Name"
        };

        _continueButton = new Button
        {
            Name = "ContinueButton",
            Left = 260,
            Top = 22,
            Width = 100,
            Text = "Continue",
            TabIndex = 1
        };

        _greetingLabel = new Label
        {
            Name = "GreetingLabel",
            Left = 24,
            Top = 72,
            Width = 320,
            Text = "Waiting"
        };

        _continueButton.Click += (_, _) => _greetingLabel.Text = $"Hello {_nameInput.Text}";

        Controls.Add(promptLabel);
        Controls.Add(_nameInput);
        Controls.Add(_continueButton);
        Controls.Add(_greetingLabel);
    }
}

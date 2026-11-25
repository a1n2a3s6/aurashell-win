using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AuraShell.Services;

namespace AuraShell.Modules.Widgets.Cards
{
    public partial class AppleRemindersWidget : UserControl, IAppleWidgetCard
    {
        private readonly ConfigStore _configStore;
        private bool _isDark = true;

        public string WidgetId => "reminders";
        public bool UsesCustomSkyBackdrop => false;

        public AppleRemindersWidget(ConfigStore configStore)
        {
            InitializeComponent();
            _configStore = configStore;
            _configStore.ConfigChanged += (_, _) => Dispatcher.Invoke(RefreshList);
            RefreshList();
        }

        public void RefreshList()
        {
            RemindersPanel.Children.Clear();
            var items = _configStore.Current.Todos;
            int remaining = items.Count(t => !t.IsDone);
            RemainingCountText.Text = remaining.ToString();
            EmptyHintText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            Brush activeFg = _isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(28, 28, 30));
            Brush doneFg = new SolidColorBrush(Color.FromRgb(142, 142, 147));
            Brush dividerBrush = _isDark
                ? new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x00, 0x00));

            foreach (var item in items.ToList())
            {
                string capturedId = item.Id;

                var rowBorder = new Border
                {
                    BorderBrush = dividerBrush,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(0, 5, 0, 5)
                };

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Apple circular radio-style check button
                var circleBtn = new Button
                {
                    Width = 18,
                    Height = 18,
                    Margin = new Thickness(0, 0, 9, 0),
                    Cursor = Cursors.Hand,
                    Background = item.IsDone ? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)) : Brushes.Transparent,
                    BorderBrush = item.IsDone
                        ? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF))
                        : new SolidColorBrush(Color.FromRgb(142, 142, 147)),
                    BorderThickness = new Thickness(1.6),
                    Content = item.IsDone ? "✓" : ""
                };
                circleBtn.Template = CreateCircleButtonTemplate();
                circleBtn.Click += (_, _) =>
                {
                    _configStore.Update(c =>
                    {
                        var target = c.Todos.FirstOrDefault(t => t.Id == capturedId);
                        if (target != null) target.IsDone = !target.IsDone;
                    });
                };
                Grid.SetColumn(circleBtn, 0);

                var label = new TextBlock
                {
                    Text = item.Text,
                    FontSize = 12.5,
                    Foreground = item.IsDone ? doneFg : activeFg,
                    TextDecorations = item.IsDone ? TextDecorations.Strikethrough : null,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(label, 1);

                var delBtn = new Button
                {
                    Content = "×",
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = doneFg,
                    FontSize = 13,
                    Padding = new Thickness(5, 0, 2, 0),
                    Cursor = Cursors.Hand
                };
                delBtn.Click += (_, _) =>
                {
                    _configStore.Update(c => c.Todos.RemoveAll(t => t.Id == capturedId));
                };
                Grid.SetColumn(delBtn, 2);

                rowGrid.Children.Add(circleBtn);
                rowGrid.Children.Add(label);
                rowGrid.Children.Add(delBtn);
                rowBorder.Child = rowGrid;
                RemindersPanel.Children.Add(rowBorder);
            }
        }

        private static ControlTemplate CreateCircleButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            presenter.SetValue(TextBlock.FontSizeProperty, 10.5);
            presenter.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);

            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        private void AddBtn_Click(object sender, RoutedEventArgs e) => AddReminder();

        private void NewReminderBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                AddReminder();
            }
        }

        private void NewReminderBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            AddPlaceholder.Visibility = string.IsNullOrEmpty(NewReminderBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddReminder()
        {
            string text = NewReminderBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            _configStore.Update(c => c.Todos.Insert(0, new TodoItem { Text = text, IsDone = false }));
            NewReminderBox.Text = string.Empty;
        }

        public void ApplySize(AppleWidgetSize size)
        {
            TitleText.FontSize = size == AppleWidgetSize.Small ? 13.5 : 15;
        }

        public void ApplyTheme(bool isDark)
        {
            _isDark = isDark;
            RemainingCountText.Foreground = isDark
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(28, 28, 30));
            NewReminderBox.Foreground = isDark
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(28, 28, 30));
            InputChrome.Background = isDark
                ? new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0x00, 0x00));
            RefreshList();
        }
    }
}

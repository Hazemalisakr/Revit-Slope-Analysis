using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Grid = System.Windows.Controls.Grid;
using Color = System.Windows.Media.Color;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SlopeAnalysis
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            string tabName = "KAITECH-BD-R10";
            try { app.CreateRibbonTab(tabName); } catch { }

            RibbonPanel panel;
            try
            {
                panel = app.CreateRibbonPanel(tabName, "Architecture");
            }
            catch
            {
                panel = app.GetRibbonPanels(tabName).First(p => p.Name == "Architecture");
            }

            string path = Assembly.GetExecutingAssembly().Location;
            var data = new PushButtonData("SlopeAnalysis", "Slope\nAnalysis", path,
                "SlopeAnalysis.SlopeAnalysisCommand")
            {
                ToolTip = "Analyze floor slopes and visualize with color coding"
            };

            var btn = panel.AddItem(data) as PushButton;
            var icon = LoadResource("SlopeAnalysis.Resources.slope_icon.png");
            if (icon != null) btn.LargeImage = icon;

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        private BitmapImage LoadResource(string name)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = stream;
            bmp.EndInit();
            return bmp;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SlopeAnalysisCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var window = new SlopeWindow(commandData.Application.ActiveUIDocument);
            var helper = new System.Windows.Interop.WindowInteropHelper(window);
            helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            window.ShowDialog();
            return Result.Succeeded;
        }
    }

    public class SlopeWindow : Window
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private List<ElementId> _floorIds = new List<ElementId>();
        private TextBlock _lblCount;
        private System.Windows.Controls.TextBox _txtStart;
        private System.Windows.Controls.TextBox _txtEnd;

        public SlopeWindow(UIDocument uidoc)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            BuildUI();
        }

        private void BuildUI()
        {
            Title = "Slope Analysis";
            Width = 460;
            Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Content = grid;

            var header = new Border { Background = new SolidColorBrush(Color.FromRgb(33, 33, 33)) };
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.Child = headerGrid;

            var btnSelect = new Button
            {
                Content = "Select Floors",
                Background = new SolidColorBrush(Color.FromRgb(0, 153, 76)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(15, 12, 0, 12),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 13
            };
            btnSelect.Click += OnSelect;
            Grid.SetColumn(btnSelect, 0);
            headerGrid.Children.Add(btnSelect);

            _lblCount = new TextBlock
            {
                Text = "Selected: 0",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 0, 0, 0)
            };
            Grid.SetColumn(_lblCount, 1);
            headerGrid.Children.Add(_lblCount);

            var body = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
            Grid.SetRow(body, 1);
            grid.Children.Add(body);

            var title = new TextBlock
            {
                Text = "Define Slope Range (%)",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15)
            };
            body.Children.Add(title);

            var inputsGrid = new Grid { Margin = new Thickness(40, 0, 40, 15) };
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            var spStart = new StackPanel();
            Grid.SetColumn(spStart, 0);
            spStart.Children.Add(new TextBlock { Text = "Start Range:", Margin = new Thickness(0,0,0,5) });
            _txtStart = new System.Windows.Controls.TextBox { Text = "0", Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5,0,0,0) };
            spStart.Children.Add(_txtStart);
            inputsGrid.Children.Add(spStart);

            var spEnd = new StackPanel();
            Grid.SetColumn(spEnd, 2);
            spEnd.Children.Add(new TextBlock { Text = "End Range:", Margin = new Thickness(0,0,0,5) });
            _txtEnd = new System.Windows.Controls.TextBox { Text = "0", Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5,0,0,0) };
            spEnd.Children.Add(_txtEnd);
            inputsGrid.Children.Add(spEnd);

            body.Children.Add(inputsGrid);

            var legendPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 20) };
            
            legendPanel.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0, 153, 76)), Width = 16, Height = 16, Margin = new Thickness(0,0,8,0) });
            legendPanel.Children.Add(new TextBlock { Text = "In-Range Slope", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,40,0) });

            legendPanel.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(180, 30, 30)), Width = 16, Height = 16, Margin = new Thickness(0,0,8,0) });
            legendPanel.Children.Add(new TextBlock { Text = "Out-of-Range Slope", VerticalAlignment = VerticalAlignment.Center });

            body.Children.Add(legendPanel);

            var btnAnalysis = new Button
            {
                Content = "Analysis",
                Background = new SolidColorBrush(Color.FromRgb(0, 153, 76)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Height = 36,
                Width = 240,
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 13
            };
            btnAnalysis.Click += OnAnalysis;
            body.Children.Add(btnAnalysis);

            var btnReset = new Button
            {
                Content = "Reset",
                Background = new SolidColorBrush(Color.FromRgb(180, 30, 30)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Height = 36,
                Width = 240,
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 13
            };
            btnReset.Click += OnReset;
            body.Children.Add(btnReset);
        }

        private void OnSelect(object sender, RoutedEventArgs e)
        {
            TaskDialog.Show("Instructions", "Revit will now enter selection mode.\n\n1. Select your floor elements.\n2. Click the 'Finish' button located on the options bar just below the Revit ribbon to return here.");
            
            Hide();
            try
            {
                var refs = _uidoc.Selection.PickObjects(
                    ObjectType.Element, new FloorFilter(), "Select floor elements, then click Finish on the options bar");
                _floorIds = refs.Select(r => r.ElementId).ToList();
                _lblCount.Text = "Selected: " + _floorIds.Count;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
            ShowDialog();
        }

        private void OnAnalysis(object sender, RoutedEventArgs e)
        {
            if (_floorIds.Count == 0)
            {
                TaskDialog.Show("Slope Analysis", "Please select floors first.");
                return;
            }

            if (!double.TryParse(_txtStart.Text, out double lo) ||
                !double.TryParse(_txtEnd.Text, out double hi))
            {
                TaskDialog.Show("Slope Analysis", "Enter valid slope range values.");
                return;
            }

            using (var tx = new Transaction(_doc, "Slope Analysis"))
            {
                tx.Start();
                ElementId greenId = ResolveOrCreateMaterial("SA_InRange",
                    new Autodesk.Revit.DB.Color(0, 153, 76));
                ElementId redId = ResolveOrCreateMaterial("SA_OutOfRange",
                    new Autodesk.Revit.DB.Color(180, 30, 30));

                var opts = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };

                foreach (ElementId id in _floorIds)
                {
                    var geom = _doc.GetElement(id)?.get_Geometry(opts);
                    if (geom == null) continue;

                    foreach (var solid in Solids(geom))
                    {
                        foreach (Face face in solid.Faces)
                        {
                            var bb = face.GetBoundingBox();
                            var uv = new UV((bb.Min.U + bb.Max.U) / 2.0, (bb.Min.V + bb.Max.V) / 2.0);
                            double slope = SlopePercent(face.ComputeNormal(uv));
                            _doc.Paint(id, face, slope >= lo && slope <= hi ? greenId : redId);
                        }
                    }
                }
                tx.Commit();
            }
        }

        private void OnReset(object sender, RoutedEventArgs e)
        {
            if (_floorIds.Count == 0) return;

            using (var tx = new Transaction(_doc, "Reset Slope Analysis"))
            {
                tx.Start();
                var opts = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };

                foreach (ElementId id in _floorIds)
                {
                    var geom = _doc.GetElement(id)?.get_Geometry(opts);
                    if (geom == null) continue;

                    foreach (var solid in Solids(geom))
                        foreach (Face face in solid.Faces)
                            if (_doc.IsPainted(id, face))
                                _doc.RemovePaint(id, face);
                }
                tx.Commit();
            }
        }

        private double SlopePercent(XYZ n)
        {
            double nz = Math.Abs(n.Z);
            if (nz < 1e-10) return double.MaxValue;
            return Math.Sqrt(n.X * n.X + n.Y * n.Y) / nz * 100.0;
        }

        private ElementId ResolveOrCreateMaterial(string name, Autodesk.Revit.DB.Color color)
        {
            var mat = new FilteredElementCollector(_doc)
                .OfClass(typeof(Material)).Cast<Material>()
                .FirstOrDefault(m => m.Name == name);

            if (mat != null) return mat.Id;

            ElementId id = Material.Create(_doc, name);
            var created = _doc.GetElement(id) as Material;
            created.Color = color;

            var fill = new FilteredElementCollector(_doc)
                .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);

            if (fill != null)
            {
                created.SurfaceForegroundPatternId = fill.Id;
                created.SurfaceForegroundPatternColor = color;
            }

            return id;
        }

        private IEnumerable<Solid> Solids(GeometryElement geom)
        {
            foreach (GeometryObject obj in geom)
            {
                if (obj is Solid s && s.Faces.Size > 0) yield return s;
                else if (obj is GeometryInstance gi)
                    foreach (var inner in Solids(gi.GetInstanceGeometry()))
                        yield return inner;
            }
        }
    }

    public class FloorFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is Floor;
        public bool AllowReference(Reference r, XYZ p) => false;
    }
}

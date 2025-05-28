using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Input;

namespace AntFarm
{
    public partial class MainWindow : Window
    {
        private readonly Random random = new Random();
        private readonly List<Ant> ants = new List<Ant>();
        private readonly List<SandCell> sandGrid = new List<SandCell>();
        private DispatcherTimer timer = new DispatcherTimer();
        private const int AntCount = 30; // More ants
        private const int GridSize = 4; // Smaller grid for more precise digging
        private const double SandDensity = 1.0;
        private bool isSpeedBoosted = false;
        private DateTime lastUpdateTime;
        private readonly Dictionary<(int, int), Rectangle> sandShapes = new Dictionary<(int, int), Rectangle>();
        private readonly Dictionary<Ant, Ellipse> antShapes = new Dictionary<Ant, Ellipse>();

        public MainWindow()
        {
            InitializeComponent();
            
            // Enable window dragging
            this.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                    this.DragMove();
            };

            // Add key handlers for speed boost
            this.KeyDown += (s, e) =>
            {
                if (e.Key == Key.P && !isSpeedBoosted)
                {
                    isSpeedBoosted = true;
                    foreach (var ant in ants)
                    {
                        ant.SetSpeedBoost(true);
                    }
                }
            };

            this.KeyUp += (s, e) =>
            {
                if (e.Key == Key.P && isSpeedBoosted)
                {
                    isSpeedBoosted = false;
                    foreach (var ant in ants)
                    {
                        ant.SetSpeedBoost(false);
                    }
                }
            };

            // Wait for canvas to be properly sized
            this.Loaded += (s, e) =>
            {
                // Initialize sand grid
                InitializeSandGrid();
                
                // Initialize ants at random positions at the top
                for (int i = 0; i < AntCount; i++)
                {
                    var ant = new Ant(
                        random.Next(0, (int)AntFarmCanvas.ActualWidth),
                        random.Next(0, 20),
                        this
                    );
                    ants.Add(ant);
                    
                    // Create and store ant shape
                    var antShape = new Ellipse
                    {
                        Width = 6,
                        Height = 6,
                        Fill = Brushes.Black
                    };
                    antShapes[ant] = antShape;
                    AntFarmCanvas.Children.Add(antShape);
                }

                lastUpdateTime = DateTime.Now;
                timer.Interval = TimeSpan.FromMilliseconds(16);
                timer.Tick += Timer_Tick;
                timer.Start();
            };
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void InitializeSandGrid()
        {
            sandGrid.Clear();
            sandShapes.Clear();
            int gridWidth = (int)(AntFarmCanvas.ActualWidth / GridSize);
            int gridHeight = (int)(AntFarmCanvas.ActualHeight / GridSize);

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    var cell = new SandCell(x, y, true);
                    sandGrid.Add(cell);
                    
                    // Create and store sand shape
                    var sandShape = new Rectangle
                    {
                        Width = GridSize,
                        Height = GridSize,
                        Fill = new SolidColorBrush(Color.FromArgb(220, 238, 214, 175))
                    };
                    System.Windows.Controls.Canvas.SetLeft(sandShape, x * GridSize);
                    System.Windows.Controls.Canvas.SetTop(sandShape, y * GridSize);
                    sandShapes[(x, y)] = sandShape;
                    AntFarmCanvas.Children.Add(sandShape);
                }
            }
        }

        public SandCell? GetSandCell(int gridX, int gridY)
        {
            return sandGrid.FirstOrDefault(c => c.GridX == gridX && c.GridY == gridY);
        }

        public SandCell? GetSandCellAtPoint(double x, double y)
        {
            int gridX = (int)(x / GridSize);
            int gridY = (int)(y / GridSize);
            return GetSandCell(gridX, gridY);
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            var currentTime = DateTime.Now;
            var deltaTime = (currentTime - lastUpdateTime).TotalMilliseconds;
            lastUpdateTime = currentTime;

            // Update all ants
            foreach (var ant in ants)
            {
                ant.Update(deltaTime);
                
                // Update ant position
                var antShape = antShapes[ant];
                System.Windows.Controls.Canvas.SetLeft(antShape, ant.X);
                System.Windows.Controls.Canvas.SetTop(antShape, ant.Y);
            }

            // Update sand visibility
            foreach (var cell in sandGrid)
            {
                var shape = sandShapes[(cell.GridX, cell.GridY)];
                shape.Visibility = cell.IsSolid ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    public class SandCell
    {
        public int GridX { get; }
        public int GridY { get; }
        public bool IsSolid { get; set; }
        public int LastModified { get; set; } // Track when this cell was last modified

        public SandCell(int gridX, int gridY, bool isSolid)
        {
            GridX = gridX;
            GridY = gridY;
            IsSolid = isSolid;
            LastModified = 0;
        }
    }

    public class Ant
    {
        public double X { get; private set; }
        public double Y { get; private set; }
        private double targetX;
        private double targetY;
        private double direction;
        private readonly Random random = new Random();
        private readonly MainWindow window;
        private int stuckCounter = 0;
        private int age = 0;
        private const double DownwardBias = 0.3;
        private const int MaxStuckTime = 1;
        private const double BaseMovementSpeed = 0.2;
        private double currentMovementSpeed;
        private const double DigDistance = 1.0;
        private const double MoveDistance = 1.0;
        private bool isSpeedBoosted = false;
        private double updateAccumulator = 0;
        private const double UpdateInterval = 16.0;
        private double lastX;
        private double lastY;
        private double previousX; // Added for lerping
        private double previousY; // Added for lerping
        private const double MaxTunnelPreference = 0.5;
        private const double TunnelPreferenceGrowth = 0.0001;
        private const double HeightPreferenceMultiplier = 0.002;
        private const double MinHeightForPreference = 100.0;
        private const double MovementSmoothing = 0.1;

        public Ant(double x, double y, MainWindow window)
        {
            X = x;
            Y = y;
            lastX = x;
            lastY = y;
            previousX = x; // Initialize previous position
            previousY = y; // Initialize previous position
            targetX = x;
            targetY = y;
            this.window = window;
            direction = random.NextDouble() * Math.PI * 2;
            currentMovementSpeed = BaseMovementSpeed;
        }

        private bool ShouldPreferTunnels()
        {
            // Calculate base preference from age
            double agePreference = Math.Min(0.4, age * TunnelPreferenceGrowth * (isSpeedBoosted ? 50 : 1));
            
            // Calculate height-based preference
            double heightPreference = 0;
            if (Y > MinHeightForPreference)
            {
                // Normalize height preference (0 to 1) based on how far down they are
                heightPreference = Math.Min(0.4, (Y - MinHeightForPreference) * HeightPreferenceMultiplier);
            }
            
            // Combine preferences, but cap at MaxTunnelPreference
            double totalPreference = Math.Min(MaxTunnelPreference, agePreference + heightPreference);
            
            return random.NextDouble() < totalPreference;
        }

        private (double x, double y)? FindNearestTunnel()
        {
            // Increase search radius based on height
            double baseSearchRadius = 20.0;
            double heightMultiplier = Math.Max(1.0, Y / 200.0); // Increase radius as they go deeper
            double searchRadius = baseSearchRadius * heightMultiplier;
            
            double bestDistance = double.MaxValue;
            (double x, double y)? bestPoint = null;

            // Search in a grid pattern around the ant
            for (double dx = -searchRadius; dx <= searchRadius; dx += 2.0)
            {
                for (double dy = -searchRadius; dy <= searchRadius; dy += 2.0)
                {
                    double checkX = X + dx;
                    double checkY = Y + dy;
                    
                    // Check if this point is a tunnel (not solid)
                    var cell = window.GetSandCellAtPoint(checkX, checkY);
                    if (cell != null && !cell.IsSolid)
                    {
                        double distance = Math.Sqrt(dx * dx + dy * dy);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestPoint = (checkX, checkY);
                        }
                    }
                }
            }

            return bestPoint;
        }

        private void DigLine(double x1, double y1, double x2, double y2)
        {
            // Calculate distance between points
            double dx = x2 - x1;
            double dy = y2 - y1;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            
            // Number of points to check based on distance
            int steps = Math.Max(1, (int)(distance / 2.0));
            
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double x = x1 + dx * t;
                double y = y1 + dy * t;
                
                // Dig at this point and surrounding cells
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    for (int offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        var cell = window.GetSandCellAtPoint(x + offsetX, y + offsetY);
                        if (cell != null && cell.IsSolid)
                        {
                            cell.IsSolid = false;
                        }
                    }
                }
            }
        }

        public void SetSpeedBoost(bool boosted)
        {
            isSpeedBoosted = boosted;
            currentMovementSpeed = boosted ? BaseMovementSpeed * 50 : BaseMovementSpeed;
        }

        public void Update(double deltaTime)
        {
            updateAccumulator += deltaTime;
            
            if (updateAccumulator >= UpdateInterval)
            {
                updateAccumulator = 0;
                age += isSpeedBoosted ? 50 : 1;

                // Store previous position before updating
                previousX = X;
                previousY = Y;

                if (ShouldPreferTunnels())
                {
                    var nearestTunnel = FindNearestTunnel();
                    if (nearestTunnel.HasValue)
                    {
                        double dx = nearestTunnel.Value.x - X;
                        double dy = nearestTunnel.Value.y - Y;
                        direction = Math.Atan2(dy, dx);
                    }
                    else
                    {
                        if (random.NextDouble() < DownwardBias)
                        {
                            direction = Math.PI / 2 + (random.NextDouble() - 0.5) * Math.PI;
                        }
                        else
                        {
                            direction += (random.NextDouble() - 0.5) * Math.PI;
                        }
                    }
                }
                else
                {
                    if (random.NextDouble() < DownwardBias)
                    {
                        direction = Math.PI / 2 + (random.NextDouble() - 0.5) * Math.PI;
                    }
                    else
                    {
                        direction += (random.NextDouble() - 0.5) * Math.PI;
                    }
                }

                // Try to dig
                double lookAheadX = X + Math.Cos(direction) * DigDistance;
                double lookAheadY = Y + Math.Sin(direction) * DigDistance;
                var cellAhead = window.GetSandCellAtPoint(lookAheadX, lookAheadY);
                if (cellAhead != null && cellAhead.IsSolid)
                {
                    cellAhead.IsSolid = false;
                    stuckCounter = 0;
                }

                // Calculate new target
                targetX = X + Math.Cos(direction) * MoveDistance;
                targetY = Y + Math.Sin(direction) * MoveDistance;
            }

            // Store current position before moving
            lastX = X;
            lastY = Y;

            // Always update position smoothly with lerping
            var targetCell = window.GetSandCellAtPoint(targetX, targetY);
            if (targetCell == null || !targetCell.IsSolid)
            {
                // Simplified movement calculation
                double lerpFactor = MovementSmoothing * (deltaTime / 16.0);
                X = previousX + (targetX - previousX) * lerpFactor;
                Y = previousY + (targetY - previousY) * lerpFactor;
                
                stuckCounter = 0;

                // Dig a tunnel between last position and new position
                DigLine(lastX, lastY, X, Y);
            }
            else
            {
                stuckCounter++;
                if (stuckCounter > MaxStuckTime)
                {
                    direction = random.NextDouble() * Math.PI * 2;
                    stuckCounter = 0;
                }
            }

            // Keep ant within bounds
            X = Math.Max(0, Math.Min(X, 760));
            Y = Math.Max(0, Math.Min(Y, 560));
            targetX = Math.Max(0, Math.Min(targetX, 760));
            targetY = Math.Max(0, Math.Min(targetY, 560));
        }
    }
} 
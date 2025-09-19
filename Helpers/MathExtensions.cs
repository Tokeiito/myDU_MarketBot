using System;

namespace MarketBot.Helpers
{
    /// <summary>
    /// Mathematical utility extensions for World Model calculations
    /// </summary>
    public static class MathExtensions
    {
        private static readonly object _lockObject = new object();
        private static bool _hasSpareGaussian = false;
        private static double _spareGaussian = 0.0;

        /// <summary>
        /// Generate a random number from a standard normal distribution (mean=0, stddev=1)
        /// using the Box-Muller transform. This is thread-safe.
        /// </summary>
        /// <param name="random">Random number generator instance</param>
        /// <returns>Gaussian random number with mean=0 and standard deviation=1</returns>
        public static double NextGaussian(this Random random)
        {
            lock (_lockObject)
            {
                // Box-Muller transform generates two independent Gaussian random numbers
                // We cache one for the next call to improve efficiency
                if (_hasSpareGaussian)
                {
                    _hasSpareGaussian = false;
                    return _spareGaussian;
                }

                _hasSpareGaussian = true;

                // Generate two uniform random numbers in (0,1)
                double u, v, s;
                do
                {
                    u = 2.0 * random.NextDouble() - 1.0; // [-1, 1)
                    v = 2.0 * random.NextDouble() - 1.0; // [-1, 1)
                    s = u * u + v * v;
                } while (s >= 1.0 || s == 0.0); // Ensure we're inside unit circle

                // Box-Muller transformation
                double multiplier = Math.Sqrt(-2.0 * Math.Log(s) / s);
                _spareGaussian = v * multiplier;
                return u * multiplier;
            }
        }

        /// <summary>
        /// Generate a Gaussian random number with specified mean and standard deviation
        /// </summary>
        /// <param name="random">Random number generator instance</param>
        /// <param name="mean">Desired mean of the distribution</param>
        /// <param name="stddev">Desired standard deviation of the distribution</param>
        /// <returns>Gaussian random number with specified parameters</returns>
        public static double NextGaussian(this Random random, double mean, double stddev)
        {
            return mean + stddev * random.NextGaussian();
        }

        /// <summary>
        /// Clamp a value between minimum and maximum bounds
        /// </summary>
        /// <param name="value">Value to clamp</param>
        /// <param name="min">Minimum allowed value</param>
        /// <param name="max">Maximum allowed value</param>
        /// <returns>Clamped value</returns>
        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// Calculate a smooth seasonal factor based on day of year
        /// </summary>
        /// <param name="dayOfYear">Day of year (1-365)</param>
        /// <param name="amplitude">Amplitude of seasonal variation</param>
        /// <returns>Seasonal factor between -amplitude and +amplitude</returns>
        public static double CalculateSeasonalFactor(int dayOfYear, double amplitude)
        {
            // Convert day of year to radians (0 to 2π)
            double yearProgress = (dayOfYear / 365.0) * 2.0 * Math.PI;
            return amplitude * Math.Sin(yearProgress);
        }

        /// <summary>
        /// Calculate current seasonal factor based on current date
        /// </summary>
        /// <param name="amplitude">Amplitude of seasonal variation</param>
        /// <returns>Seasonal factor between -amplitude and +amplitude</returns>
        public static double GetCurrentSeasonalFactor(double amplitude)
        {
            return CalculateSeasonalFactor(DateTime.UtcNow.DayOfYear, amplitude);
        }
    }
}
﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Playnite.Database;
using NLog;
using System.IO;
using Playnite;

namespace PlayniteUI
{
    public class LiteDBImageToImageConverter : IValueConverter
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public static bool IsCacheEnabled
        {
            get; set;
        } = false;

        public static Dictionary<string, BitmapImage> Cache
        {
            get; set;
        } = new Dictionary<string, BitmapImage>();

        public static void ClearCache()
        {
            Cache.Clear();
        }

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
            {
                return DependencyProperty.UnsetValue;
            }            

            var imageId = (string)value;
            if (string.IsNullOrEmpty(imageId))
            {
                return DependencyProperty.UnsetValue;
            }

            if (imageId.StartsWith("resources:"))
            {
                return imageId.Replace("resources:", "");
            }

            if (imageId.StartsWith("http", StringComparison.InvariantCultureIgnoreCase))
            {
                var cachedFile = Web.GetCachedWebFile(imageId);
                if (string.IsNullOrEmpty(cachedFile))
                {
                    logger.Warn("Web file not found: " + imageId);
                    return DependencyProperty.UnsetValue;
                }

                return BitmapExtensions.BitmapFromFile(cachedFile);
            }

            if (File.Exists(imageId))
            {
                return BitmapExtensions.BitmapFromFile(imageId);
            }

            if (IsCacheEnabled && Cache.ContainsKey(imageId))
            {
                return Cache[imageId];
            }
            else
            {
                try
                {
                    var imageData = App.Database.GetFileImage(imageId);
                    if (imageData == null)
                    {
                        logger.Warn("Image not found in database: " + imageId);
                        return DependencyProperty.UnsetValue;
                    }
                    else
                    {
                        if (IsCacheEnabled)
                        {
                            Cache.Add(imageId, imageData);
                        }

                        return imageData;
                    }
                }
                catch (Exception exc) when (!PlayniteEnvironment.ThrowAllErrors)
                {
                    logger.Error(exc, "Failed to load image from database.");
                    return DependencyProperty.UnsetValue;
                }
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}

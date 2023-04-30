// See https://aka.ms/new-console-template for more information

using MaxRev.Gdal.Core;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Runtime.CompilerServices;

/**
     * 瓦片大小
     */
int TILE_SIZE = 256;
/**
 * web 墨卡托 EPSG 编码
 */
int WEB_MERCATOR_EPSG_CODE = 3857;
/**
 * Wgs84 EPSG 编码
 */
int WGS_84_EGPS_CODE = 4326;

GdalBase.ConfigureAll();

Tif2Tiles("G:\\demo\\3=tdom-f.tif", "data", 10, 18);

/**
 * Tif 切片
 *
 * @param tifFile
 * @param outputDir
 * @param minZoom
 * @param maxZoom
 */
void Tif2Tiles(string tifFile, string outputDir, int minZoom, int maxZoom)
{
    string tmpFile = "data\\temp.tif";

    using var srcDs = Gdal.Open(tifFile, Access.GA_ReadOnly);
    double[] srcGeoTransform = new double[6];
    srcDs.GetGeoTransform(srcGeoTransform);
    if (srcDs == null)
    {
        throw new FileNotFoundException();
    }
    // 1. 影像重投影到web墨卡托
    var spatialReference = new SpatialReference("");
    spatialReference.ImportFromEPSG(WEB_MERCATOR_EPSG_CODE);
    spatialReference.ExportToWkt(out string wkt, null);

    var srcSr = new SpatialReference("");
    srcSr.ImportFromEPSG(4528);
    srcSr.SetTM(0, 120, 1, 500000, 0);
    srcSr.ExportToWkt(out var srcWkt, null);

    if (File.Exists(tmpFile))
        File.Delete(tmpFile);
    var options = Gdal.ParseCommandLine($"-t_srs EPSG:3857 -r near -of GTiff");
    Gdal.Warp(tmpFile, new[] { srcDs }, new GDALWarpAppOptions(options), null, null);
    var dataset = Gdal.Open(tmpFile, Access.GA_ReadOnly);

    try
    {
        // 2. 获取遥感影像经纬度范围，计算遥感影像像素分辨率
        // 获取原始影像的地理坐标范围

        double[] geoTransform = new double[6];
        dataset.GetGeoTransform(geoTransform);

        // 获取原始影像的像素分辨率
        int xSize = dataset.RasterXSize;
        int ySize = dataset.RasterYSize;
        // 计算经纬度范围
        double lngMin = geoTransform[0];
        double latMax = geoTransform[3];
        double lngMax = lngMin + (xSize * geoTransform[1]) + (ySize * geoTransform[2]);
        double latMin = latMax + (xSize * geoTransform[4]) + (ySize * geoTransform[5]);
        // EPSG:3857 坐标转Wgs84经纬度
        var sourceCRS = new SpatialReference("");
        sourceCRS.ImportFromEPSG(WEB_MERCATOR_EPSG_CODE);

        var targetCRS = new SpatialReference("");
        targetCRS.ImportFromEPSG(WGS_84_EGPS_CODE);

        var transform = new CoordinateTransformation(sourceCRS, targetCRS);
        var lats = new double[] { latMin, latMax };
        var lngs = new double[] { lngMin, lngMax };
        transform.TransformPoints(2, lngs, lats, new double[] { 0, 0 });

        lngMax = lngs[1];
        latMax = lats[1];
        lngMin = lngs[0];
        latMin = lats[0];
        // 原始图像东西方向像素分辨率
        double srcWePixelResolution = (lngMax - lngMin) / xSize;
        // 原始图像南北方向像素分辨率
        double srcNsPixelResolution = (latMax - latMin) / ySize;
        for (int zoom = minZoom; zoom <= maxZoom; zoom++)
        {
            // 3. 根据原始影像地理范围求解切片行列号
            int[] tilePointMax = coordinates2tile(lngMax, latMax, zoom);
            int[] tilePointMin = coordinates2tile(lngMin, latMin, zoom);
            int tileRowMax = tilePointMin[1];
            int tileColMax = tilePointMax[0];
            int tileRowMin = tilePointMax[1];
            int tileColMin = tilePointMin[0];
            for (int row = tileRowMin; row <= tileRowMax; row++)
            {
                for (int col = tileColMin; col <= tileColMax; col++)
                {
                    // 4. 求原始影像地理范围与指定缩放级别指定行列号的切片交集
                    double tempLatMin = tile2lat(row + 1, zoom);
                    double tempLatMax = tile2lat(row, zoom);
                    double tempLonMin = tile2lng(col, zoom);
                    double tempLonMax = tile2lng(col + 1, zoom);
                    var tileBound = new ReferencedEnvelope(tempLonMin, tempLonMax, tempLatMin, tempLatMax);
                    var imageBound = new ReferencedEnvelope(lngMin, lngMax, latMin, latMax);
                    var intersect = tileBound.Intersection(imageBound);
                    // 5. 求解当前切片的像素分辨率(默认切片大小为256*256)
                    // 切片东西方向像素分辨率
                    double dstWePixelResolution = (tempLonMax - tempLonMin) / 256;
                    // 切片南北方向像素分辨率
                    double dstNsPixelResolution = (tempLatMax - tempLatMin) / 256;
                    // 6. 计算交集的像素信息
                    // 求切图范围和原始图像交集的起始点像素坐标
                    int offsetX = (int)((intersect.LonMin - lngMin) / srcWePixelResolution);
                    int offsetY = (int)Math.Abs((intersect.LatMax - latMax) / srcNsPixelResolution);
                    // 求在切图地理范围内的原始图像的像素大小
                    int blockXSize = (int)((intersect.LonMax - intersect.LonMin) / srcWePixelResolution);
                    int blockYSize = (int)((intersect.LatMax - intersect.LatMin) / srcNsPixelResolution);
                    // 求原始图像在切片内的像素大小
                    int imageXBuf = (int)Math.Ceiling((intersect.LonMax - intersect.LonMin) / dstWePixelResolution);
                    int imageYBuf = (int)Math.Ceiling(Math.Abs((intersect.LatMax - intersect.LatMin) / dstNsPixelResolution));
                    // 求原始图像在切片中的偏移坐标
                    int imageOffsetX = (int)((intersect.LonMin - tempLonMin) / dstWePixelResolution);
                    int imageOffsetY = (int)Math.Abs((intersect.LatMax - tempLatMax) / dstNsPixelResolution);
                    imageOffsetX = imageOffsetX > 0 ? imageOffsetX : 0;
                    imageOffsetY = imageOffsetY > 0 ? imageOffsetY : 0;
                    // 7. 使用GDAL的ReadRaster方法对影像指定范围进行读取与压缩。
                    // 推荐在切片前建立原始影像的金字塔文件，ReadRaster在内部实现中可直接读取相应级别的金字塔文件，提高效率。
                    Band inBand1 = dataset.GetRasterBand(1);
                    Band inBand2 = dataset.GetRasterBand(2);
                    Band inBand3 = dataset.GetRasterBand(3);
                    int[] band1BuffData = new int[TILE_SIZE * TILE_SIZE * GdalConst.GDT_UInt64];
                    int[] band2BuffData = new int[TILE_SIZE * TILE_SIZE * GdalConst.GDT_UInt64];
                    int[] band3BuffData = new int[TILE_SIZE * TILE_SIZE * GdalConst.GDT_UInt64];
                    inBand1.ReadRaster(offsetX, offsetY, blockXSize, blockYSize, band1BuffData, imageXBuf, imageYBuf, GdalConst.GDT_UInt64, 0);
                    inBand2.ReadRaster(offsetX, offsetY, blockXSize, blockYSize, band2BuffData, imageXBuf, imageYBuf, GdalConst.GDT_UInt64, 0);
                    inBand3.ReadRaster(offsetX, offsetY, blockXSize, blockYSize, band3BuffData, imageXBuf, imageYBuf, GdalConst.GDT_UInt64, 0);
                    //  8. 将切片数据写入文件
                    // 使用gdal的MEM驱动在内存中创建一块区域存储图像数组
                    Driver memDriver = Gdal.GetDriverByName("MEM");
                    Dataset msmDS = memDriver.Create("msmDS", 256, 256, 4, DataType.GDT_UInt64, null);
                    Band dstBand1 = msmDS.GetRasterBand(1);
                    Band dstBand2 = msmDS.GetRasterBand(2);
                    Band dstBand3 = msmDS.GetRasterBand(3);
                    // 设置alpha波段数据,实现背景透明
                    Band alphaBand = msmDS.GetRasterBand(4);
                    int[] alphaData = new int[256 * 256 * GdalConst.GDT_UInt64];
                    for (int index = 0; index < alphaData.Length; index++)
                    {
                        if (band1BuffData[index] > 0)
                        {
                            alphaData[index] = 255;
                        }
                    }
                    // 写各个波段数据
                    dstBand1.WriteRaster(imageOffsetX, imageOffsetY, imageXBuf, imageYBuf, band1BuffData, imageXBuf, imageYBuf, GdalConst.GDT_UInt64, 0);
                    dstBand2.WriteRaster(imageOffsetX, imageOffsetY, imageXBuf, imageYBuf, band2BuffData, imageXBuf, imageYBuf, GdalConst.GDT_UInt64, 0);
                    dstBand3.WriteRaster(imageOffsetX, imageOffsetY, imageXBuf, imageYBuf, band3BuffData, imageXBuf, imageYBuf, GdalConst.GDT_UInt64, 0);
                    alphaBand.WriteRaster(imageOffsetX, imageOffsetY, imageXBuf, imageYBuf, alphaData, imageXBuf, imageYBuf, GdalConst.GDT_UInt64, 0);

                    if (!Directory.Exists(Path.Combine(outputDir, zoom.ToString())))
                        Directory.CreateDirectory(Path.Combine(outputDir, zoom.ToString()));
                    if (!Directory.Exists(Path.Combine(outputDir, zoom.ToString(), col.ToString())))
                        Directory.CreateDirectory(Path.Combine(outputDir, zoom.ToString(), col.ToString()));

                    var pngPath = Path.Combine(outputDir, zoom.ToString(), col.ToString(), row + ".png");
                    // 使用PNG驱动将内存中的图像数组写入文件
                    Driver pngDriver = Gdal.GetDriverByName("PNG");
                    Dataset pngDs = pngDriver.CreateCopy(pngPath, msmDS, 0, null, null, null);
                    // 释放内存
                    msmDS.FlushCache();
                    msmDS.Dispose();
                    pngDs.Dispose();
                }
            }
        }
    }
    finally
    {
        // 释放并删除临时文件
        dataset.Dispose();
    }
}
/**
 * 经纬度坐标转瓦片坐标
 *
 * @param lng
 * @param lat
 * @param zoom
 * @return
 */
int[] coordinates2tile(double lng, double lat, int zoom)
{
    var n = Math.Pow(2, zoom);
    var tileX = (lng + 180) / 360 * n;
    var tileY = (1 - (Math.Log(Math.Tan(ToRadians(lat)) + (1 / Math.Cos(ToRadians(lat)))) / Math.PI)) / 2 * n;
    return new int[] { (int)Math.Floor(tileX), (int)Math.Floor(tileY) };
}
/**
 * 瓦片坐标转经度
 *
 * @param x
 * @param z
 * @return
 */
double tile2lng(double x, int z)
{
    return x / Math.Pow(2.0, z) * 360.0 - 180;
}
/**
 * 瓦片坐标转纬度
 *
 * @param y
 * @param z
 * @return
 */
double tile2lat(double y, int z)
{
    double n = Math.PI - (2.0 * Math.PI * y) / Math.Pow(2.0, z);
    return ToDegress(Math.Atan(Math.Sinh(n)));
}

double ToDegress(double value)
{
    return value * 180 / Math.PI;
}

double ToRadians(double value)
{
    return value * Math.PI / 180;
}
#pragma warning disable 649
using System;
using System.IO;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine;
using UnityEngine.UI;

public class AssetDownloaderAndLoader : MonoBehaviour
{
    //Reference for the latest loaded GameObject
    private GameObject loadedGameObject;
    public string urlBase = "https://vrcat-assets.s3.us-east-2.amazonaws.com/";

    /**
    * Returns an array: first value is the GUID; second value is the file name and extension.
    */
    private string[] getAssetParameters(string url) {
        string urlSuffix = url.Substring(urlBase.Length);
        string[] assetParams = urlSuffix.Split('/');
        return assetParams;
    }
    
    /** 
     * Creates a directory to store the model in and then passes asset data onto a download coroutine.
     */
    public IEnumerator GetFileFromURL(AssetDataTemplate.AssetImportData assetData, Text progressDisplay, int index, System.Action<GameObject> callback)
    {
        //Gets guid and filename and extension       
        string[] assetParameters = getAssetParameters(assetData.url); 
        var guid = assetParameters[0];
        var fileNameAndExtension = assetParameters[1];

        //Create a unique directory based on the guid
        var localFilePath = string.Format("{0}/{1}", Application.persistentDataPath, guid);

        Directory.CreateDirectory(localFilePath);

        var localPathAndFilename = string.Format("{0}/{1}", localFilePath, fileNameAndExtension);

        yield return StartCoroutine(DownloadFile(assetData, progressDisplay, index, localPathAndFilename, callback));
    }

    /** 
    * Downloads a file to a local path, then loads the file
    */
    private IEnumerator DownloadFile(AssetDataTemplate.AssetImportData assetData, Text progressDisplay, int index, string localPathAndFilename, System.Action<GameObject> callback = null)
    {
        UnityWebRequest fileDownloader = UnityWebRequest.Get(assetData.url);

        //get size of asset first to allocate what is needed
        long sizeOfAsset = 0;
        yield return StartCoroutine(GetFileSize(assetData.url, (size) =>
            {
                sizeOfAsset = size;
            })
        );

        //set our asset download settings
        fileDownloader.method = UnityWebRequest.kHttpVerbGET;
        var dh = new DownloadHandlerFile(localPathAndFilename);
        dh.removeFileOnAbort = true;
        fileDownloader.downloadHandler = dh;
        fileDownloader.SendWebRequest();

        string assetName = assetData.name;

        while (!fileDownloader.isDone)
        {
            progressDisplay.text = $"Downloading {assetName}: {fileDownloader.downloadProgress.ToString("P")}";
            yield return null;
        }

        if (fileDownloader.isNetworkError || fileDownloader.isHttpError)
        {
            Debug.Log(fileDownloader.error);
        }
        else
        {
            //if searching for a local file from indexDB, look in here
            Debug.Log($"Download saved to: {localPathAndFilename.Replace("/", "\\")}\r\n{fileDownloader.error}");
        }

        Debug.Log($"Successfully downloaded asset {assetName}, size {fileDownloader.downloadedBytes} bytes.");

        if (callback != null)
        {
            callback(LoadLocalFile(localPathAndFilename));
        }
        else
        {
            LoadLocalFile(localPathAndFilename);
        }

        fileDownloader = null;
    }

    /**
    * Use an inherited class to replace this function.
    * Loads a local 3D model file from disk into memory and returns it as a GameObject.
    */
    public virtual GameObject LoadLocalFile(string localFilename)
    {
        return loadedGameObject = null;
    }

    public static IEnumerator GetFileSize(string url, Action<long> callback)
    {
        UnityWebRequest request = UnityWebRequest.Head(url);
        yield return request.SendWebRequest();
        string size = request.GetResponseHeader("Content-Length");

        if (request.isNetworkError || request.isHttpError)
        {
            Debug.Log("Error While Getting Length: " + request.error);
            if (callback != null) callback(-1);
        }
        else
        {
            if (callback != null) callback(Convert.ToInt64(size));
        }
    }
}

#pragma warning restore 649



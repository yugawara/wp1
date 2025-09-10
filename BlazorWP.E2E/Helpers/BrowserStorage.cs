using Microsoft.Playwright;

namespace BlazorWP.E2E.Helpers;

public static class BrowserStorage
{
    public static Task SetLocalAsync(IPage page, string key, string value) =>
        page.EvaluateAsync("([k,v]) => localStorage.setItem(k,v)", new object[] { key, value });

    public static Task<string?> GetLocalAsync(IPage page, string key) =>
        page.EvaluateAsync<string?>("k => localStorage.getItem(k)", key);

    public static Task<string[]> ListIdbStoresAsync(IPage page, string dbName) =>
        page.EvaluateAsync<string[]>(
            @"(db) => new Promise((resolve,reject)=>{
                const req=indexedDB.open(db);
                req.onerror=()=>reject(req.error?.message||'open failed');
                req.onsuccess=()=>resolve(Array.from(req.result.objectStoreNames));
              })", dbName);
}

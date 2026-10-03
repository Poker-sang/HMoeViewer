async () => {
    const result = (status, message) => ({status, message});
    let prepared = {};
    let locals;
    const get = key => prepared[key] ?? locals?.get(key) ?? window.locals?.get?.(key) ?? window.locals?.[key] ?? window.yunData?.[key];
    // SPA 先填充文件列表，再填充分享信息；不能仅等待文件列表就开始发送。
    let files;
    for (let i = 0; i < 60; ++i) {
        files = get('file_list');
        if (Array.isArray(files) && files.length && get('shareid') && get('share_uk')) break;
        await new Promise(resolve => setTimeout(resolve, 500));
    }
    if (!Array.isArray(files) || !files.length || !get('shareid') || !get('share_uk'))
        return result('interaction', '未取得分享文件信息。请检查提取码或分享状态，再点击“发送到客户端”。');

    // 从当前网页发现 SDK，避免固定 CDN 版本号。只读取百度官方静态资源。
    const trusted = value => {
        try { const url = new URL(value, location.href); return url.protocol === 'https:' && url.hostname === 'nd-static.bdstatic.com'; }
        catch { return false; }
    };
    const resources = [...document.scripts].map(s => s.src).concat(performance.getEntriesByType('resource').map(r => r.name)).filter(trusted);
    let SDK;
    let http;
    const isLegacy = typeof window.require?.async === 'function'
        && !resources.some(url => url.includes('/disk-share-v2/'));
    if (isLegacy) {
        // 只加载资源包并注册模块，不启动整个下载插件的页面入口。
        const load = url => new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = new URL(url, location.href).href;
            script.onload = resolve;
            script.onerror = () => reject(new Error('官网下载组件加载失败'));
            document.head.appendChild(script);
        });
        const references = [...document.scripts].flatMap(s => [s.src, ...[...s.textContent.matchAll(/["']([^"'\s]+\.js(?:\?[^"']*)?)["']/g)].map(m => new URL(m[1], location.href).href)]);
        const bundles = [...new Set(references.filter(url => trusted(url) && /\/function-widget-1\/pkg\/download-all[^/]*\.js/.test(url)))];
        let sdkUrl;
        let axiosUrl;
        for (const url of bundles) {
            const response = await fetch(url);
            if (!response.ok) continue;
            const source = await response.text();
            sdkUrl = source.match(/["']((?:https:)?\/\/nd-static\.bdstatic\.com\/m-static\/wp-download\/[^"']+\.js)["']/)?.[1];
            axiosUrl = source.match(/["']((?:https:)?\/\/nd-static\.bdstatic\.com\/m-static\/base\/thirdParty\/vue\/axios\.js)["']/)?.[1];
            if (sdkUrl && axiosUrl) break;
        }
        if (!sdkUrl || !axiosUrl) return result('failed', '旧版分享页未能发现官网下载组件，未提交任务。');
        if (!bundles.length) return result('failed', '旧版分享页未能发现官方下载资源包，未提交任务。');
        await load(bundles[0]);
        const config = window.require('function-widget-1:download/service/axios/config.js');
        await Promise.all([load(sdkUrl), load(axiosUrl)]);
        SDK = window['wp-download'];
        const axios = window.require('base:thirdParty/vue/axios.js');
        http = axios.create(config);
        const keys = ['share_uk', 'shareid', 'sign', 'timestamp', 'loginstate'];
        prepared = await new Promise(resolve => window.locals.get(...keys, (...values) => resolve(Object.fromEntries(keys.map((key, i) => [key, values[i]])))));
    } else {
        let sdkUrl = resources.find(url => /\/wp-download\.umd\.[^/]+\.js/.test(url));
        const rank = url => /\/(createCodeViewerRenderer|HomeIndex|LayoutIndex|shareSingle|shareMulti|.*Download)[^/]*\.js/.test(url) ? 0 : /preload-helper/.test(url) ? 2 : 1;
        const queue = [...new Set(resources.filter(url => /\/disk-share-v2\/js\/[^/]+\.js/.test(url)))];
        const visited = new Set();
        while (!sdkUrl && queue.length && visited.size < 32) {
            queue.sort((a, b) => rank(a) - rank(b));
            const url = queue.shift();
            if (visited.has(url)) continue;
            visited.add(url);
            const response = await fetch(url);
            if (!response.ok) continue;
            const source = await response.text();
            for (const match of source.matchAll(/["']([^"'\s]+\.js)["']/g)) {
                const reference = match[1];
                const next = reference.startsWith('disk-share-v2/')
                    ? new URL('/m-static/' + reference, url).href : new URL(reference, url).href;
                if (!trusted(next)) continue;
                if (next.includes('/wp-download.umd.')) { sdkUrl = next; break; }
                if (/\/disk-share-v2\/js\/[^/]+\.js/.test(next) && !visited.has(next) && !queue.includes(next)) queue.push(next);
            }
        }
        if (!sdkUrl) return result('failed', '当前分享页未能发现官方下载 SDK，请使用“打开链接”。');
        // 复用网页的 HTTP 和异步 locals 组件，完成官网按钮调用 SDK 前的准备。
        for (const url of new Set(resources.filter(url => /\/preload-helper[^/]*\.js/.test(url)))) {
            const common = await import(url);
            http = Object.values(common).find(value => typeof value?.get === 'function' && typeof value?.post === 'function'
                && value.defaults?.headers?.common?.['X-Requested-With'] === 'XMLHttpRequest');
            locals = Object.values(common).find(value => typeof value?.getMany === 'function' && typeof value?.snapshot === 'function');
            if (http && locals) break;
        }
        if (!http || !locals) return result('failed', '当前分享页未能发现官方请求或分享信息组件，请使用“打开链接”。');
        // sign、timestamp、jsToken 是按需加载的；同步读取时常常还是空值。
        const signing = await locals.getMany(['sign', 'timestamp', 'jsToken']);
        prepared = {...locals.snapshot(), ...signing};
        const module = await import(sdkUrl);
        SDK = module.w?.default ?? module.default;
    }
    if (typeof SDK !== 'function') return result('failed', '官方下载 SDK 接口已变化，未提交任务。');
    if (!get('sign') || !get('timestamp'))
        return result('failed', '分享下载签名尚未取得，未提交任务。请检查页面是否提示提取码、验证或分享失效。');
    let reportRequestFailure = () => {};
    const controller = new AbortController();
    window.hmoeCancelDownload = () => controller.abort();
    const request = async (method, url, data, config) => {
        const endpoint = new URL(url, location.href).pathname;
        try {
            controller.signal.throwIfAborted();
            const options = {...config, signal: controller.signal};
            const response = method === 'GET' ? await http.get(url, options) : await http.post(url, data, options);
            if (response.status >= 400) throw new Error('HTTP ' + response.status);
            if (!response.data || typeof response.data !== 'object') throw new Error('服务器未返回有效数据');
            if (/^\/api\/invoker\/(get|online)$/.test(endpoint) && response.data.errno != null && response.data.errno !== 0)
                throw new Error('errno=' + response.data.errno);
            return response.data;
        } catch (error) {
            reportRequestFailure('百度网盘请求失败：' + endpoint + '（' + String(error?.message || '网络异常') + '）');
            throw error;
        }
    };
    return await new Promise(resolve => {
        let settled = false;
        const finish = (status, message) => { if (!settled) { settled = true; controller.abort(); resolve(result(status, message)); } };
        let stage = '初始化下载 SDK';
        reportRequestFailure = message => finish('failed', message);
        const sdk = new SDK({
            uk: String(get('uk') || '0'),
            http: {
                get: (url, config) => request('GET', url, null, config),
                post: (url, data, config) => request('POST', url, data?.params && !config ? data.params : data, config)
            },
            bizConfig: {
                nativeParam: {checkuser: +get('loginstate') === 1 ? 'true' : 'false', src_from: 'wp-download_web_share', src_type: 'web_sharelink_page'},
                // SDK 通过 progress 触发 success，不能省略 progress 回调。
                progress: value => { stage = value; },
                success: () => finish('submitted', '百度网盘客户端已接收下载任务。'),
                fail: () => finish('failed', '未能确认客户端接收任务（阶段：' + stage + '），请检查客户端任务列表。'),
                error: message => finish('failed', String(message || '官方下载 SDK 请求失败。')),
                verifyCode: () => finish('verification', '百度网盘要求验证码。请通过“打开链接”在浏览器中手动完成下载验证；此次自动发送尚未成功。')
            }
        });
        if (typeof sdk._openYunGuanjiaBySchema !== 'function') {
            finish('failed', '官方 SDK 的客户端调用接口已变化。');
            return;
        }
        sdk._openYunGuanjiaBySchema = url => {
            controller.signal.throwIfAborted();
            return window.hmoeLaunchClient(url);
        };
        Promise.resolve(window.hmoeDownloadStarted?.()).then(() => {
            controller.signal.throwIfAborted();
            sdk.download({fileList: files, product: 'share', extra: {
                share_uk: get('share_uk'), share_id: get('shareid'), share_url: location.href,
                timestamp: get('timestamp'), sign: get('sign')
            }});
        }).catch(error => finish('failed', String(error?.message || '客户端调用失败。')));
    });
}

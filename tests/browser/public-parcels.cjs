const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const {chromium} = require(process.env.NEXORUTA_PLAYWRIGHT_MODULE || 'playwright');
(async () => {
    const browser = await chromium.launch({headless:true});
    try {
        const page = await browser.newPage({viewport:{width:1280,height:900}});
        const errors=[]; page.on('pageerror',e=>errors.push(e.message));
        await page.route('**/*',r=>['localhost','127.0.0.1'].includes(new URL(r.request().url()).hostname)?r.continue():r.abort());
        await page.goto(process.env.OPERATIONAL_TRACKING_URL);
        await page.getByText('Bultos 1 a 200 de 250.',{exact:true}).waitFor();
        assert.equal(await page.locator('[data-public-parcels] li').count(),200);
        await page.getByRole('button',{name:'Bultos siguientes',exact:true}).focus();
        await page.keyboard.press('Enter');
        await page.getByText('Bultos 201 a 250 de 250.',{exact:true}).waitFor();
        assert.equal(await page.locator('[data-public-parcels] li').count(),50);
        assert.match(await page.locator('[data-public-parcels] li').first().innerText(),/^Bulto 201:/);
        await page.waitForFunction(()=>document.activeElement?.id==='bultos-titulo');
        assert.equal(await page.getByRole('button',{name:'Bultos siguientes',exact:true}).isDisabled(),true);
        await page.screenshot({path:path.join(process.env.OPERATIONAL_OUTPUT,'parcels-desktop.png'),fullPage:true});
        await page.setViewportSize({width:320,height:812});
        assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);
        await page.screenshot({path:path.join(process.env.OPERATIONAL_OUTPUT,'parcels-mobile.png'),fullPage:true});
        await page.getByRole('button',{name:'Bultos anteriores',exact:true}).click();
        await page.getByText('Bultos 1 a 200 de 250.',{exact:true}).waitFor();
        assert.equal(await page.locator('[data-public-parcels] li').count(),200);
        assert.doesNotMatch(await page.locator('body').innerText(),/PRIVATE-PARCEL/);
        assert.deepEqual(errors,[]);
        await fs.writeFile(path.join(process.env.OPERATIONAL_OUTPUT,'parcels-summary.json'),JSON.stringify({realApi:true,anonymous:true,total:250,pageSize:200,secondPage:50,keyboard:true,focus:true,mobileWidth:320},null,2));
    } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});

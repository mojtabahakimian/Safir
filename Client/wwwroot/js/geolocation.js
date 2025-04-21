//window.blazorGeolocation = {
//    getCurrentPositionIP: function () {
//        const apiKey = 'e6edfeb52f4d4217ad207f0dbf7a6d42';
//        return fetch(`https://api.ipgeolocation.io/ipgeo?apiKey=${apiKey}`)
//            .then(response => response.json())
//            .then(data => {
//                if (data.latitude && data.longitude) {
//                    return {
//                        success: true,
//                        latitude: parseFloat(data.latitude),
//                        longitude: parseFloat(data.longitude),
//                        message: "موقعیت مکانی براساس IP دریافت شد."
//                    };
//                } else {
//                    return Promise.reject("خطا در دریافت موقعیت مکانی: اطلاعات ناقص");
//                }
//            })
//            .catch(error => Promise.reject("خطا در اتصال به سرویس: " + error));
//    }
//};

// File: wwwroot/js/geolocation.js
window.blazorGeolocation = {
    getCurrentPosition: function () {
        return new Promise((resolve, reject) => {
            if (!navigator.geolocation) {
                reject("مرورگر شما از دریافت موقعیت مکانی پشتیبانی نمی‌کند.");
                return;
            }

            navigator.geolocation.getCurrentPosition(
                position => {
                    // در صورت موفقیت، موقعیت و یک پیام شامل دقت (اگر موجود باشد) را برگردان
                    resolve({
                        success: true,
                        latitude: position.coords.latitude,
                        longitude: position.coords.longitude,
                        message: `موقعیت دریافت شد.${position.coords.accuracy ? ' دقت تخمینی: ' + position.coords.accuracy.toFixed(0) + ' متر' : ''}`
                    });
                },
                error => {
                    let errorMessage = "خطای نامشخص در دریافت موقعیت.";
                    switch (error.code) {
                        case error.PERMISSION_DENIED:
                            errorMessage = "دسترسی به موقعیت مکانی توسط کاربر رد شد.";
                            break;
                        case error.POSITION_UNAVAILABLE:
                            // پیام عمومی‌تر: مشکل می‌تواند از GPS، شبکه یا سرویس‌های پشتیبان باشد
                            errorMessage = "اطلاعات موقعیت مکانی در حال حاضر در دسترس نیست. لطفاً از فعال بودن GPS یا سرویس موقعیت‌یابی دستگاه اطمینان حاصل کنید و دوباره تلاش کنید.";
                            break;
                        case error.TIMEOUT:
                            errorMessage = "زمان درخواست برای دریافت موقعیت به پایان رسید. ممکن است سیگنال ضعیف باشد یا سرویس موقعیت‌یابی پاسخ ندهد.";
                            break;
                    }
                    reject(errorMessage); // فقط پیام خطا را برگردان
                },
                {
                    enableHighAccuracy: false, // درخواست دقت بالا (برای استفاده از GPS در صورت امکان)
                    timeout: 20000,          // افزایش زمان انتظار به 20 ثانیه
                    maximumAge: 0            // دریافت موقعیت جدید
                }
            );
        });
    }
};
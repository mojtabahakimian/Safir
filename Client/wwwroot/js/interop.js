window.initializeStimulsoftViewer = (containerId, reportUrl) => {
    const viewerOptions = {
        height: "100%",
        width: "100%",
        report: reportUrl // reportUrl is "data:application/pdf;base64,..."
    };
    const viewer = new Stimulsoft.Viewer.StiViewer(viewerOptions, containerId); // Error occurs here
    viewer.renderHtml(containerId);
};

window.downloadPdfFromBase64 = (base64String, fileName) => {
    try {
        const byteCharacters = atob(base64String);
        const byteNumbers = new Array(byteCharacters.length);
        for (let i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        const byteArray = new Uint8Array(byteNumbers);
        const blob = new Blob([byteArray], { type: 'application/pdf' });

        const link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = fileName;
        document.body.appendChild(link); // Required for Firefox
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(link.href); // Clean up
        console.log("PDF download initiated for:", fileName);
    } catch (e) {
        console.error("Error downloading PDF:", e);
    }
};


window.startP2Resize = function (e, resizerElement, colIndex, isCol1) {
    e.preventDefault();
    e.stopPropagation();

    const wrapper = resizerElement.closest('.p2-excel-wrapper');
    const table = wrapper.querySelector('.p2-excel-table');
    const cols = table.querySelectorAll('col');
    const targetCol = cols[colIndex];

    if (!wrapper || !targetCol) return;

    let startX = e.clientX;
    let startWidth = targetCol.offsetWidth || parseFloat(getComputedStyle(targetCol).width);

    document.body.classList.add('p2-resizing');

    const mouseMove = function (event) {
        const newWidth = startWidth + (startX - event.clientX); // منطق RTL

        if (newWidth > 60) {
            targetCol.style.width = `${newWidth}px`; // تغییر عرض تگ col

            if (isCol1) {
                // اگر ستون اول تغییر کرد، مکان چسبیدن ستون دوم را آپدیت می‌کنیم
                wrapper.style.setProperty('--sticky-offset', `${newWidth}px`);
            }
        }
    };

    const mouseUp = function () {
        document.body.classList.remove('p2-resizing');
        document.removeEventListener('mousemove', mouseMove);
        document.removeEventListener('mouseup', mouseUp);
    };

    document.addEventListener('mousemove', mouseMove);
    document.addEventListener('mouseup', mouseUp);
};
window.createPdfBlobUrl = (byteArray) => {
    const blob = new Blob([new Uint8Array(byteArray)], { type: 'application/pdf' });
    return URL.createObjectURL(blob);
};

window.revokePdfBlobUrl = (url) => {
    if (url) {
        URL.revokeObjectURL(url);
    }
};

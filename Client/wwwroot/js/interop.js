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
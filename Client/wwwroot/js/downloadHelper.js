// wwwroot/js/downloadHelper.js
window.downloadFileFromBytes = (fileName, byteArray) => {
    // Create a Blob object from the byte array
    const blob = new Blob([byteArray], { type: "application/pdf" }); // Set MIME type for PDF

    // Create a link element
    const link = document.createElement('a');

    // Set the download attribute with the desired file name
    link.download = fileName;

    // Create a URL for the Blob and set it as the href attribute
    link.href = window.URL.createObjectURL(blob);

    // Append the link to the DOM (needed for Firefox)
    document.body.appendChild(link);

    // Programmatically click the link to trigger the download
    link.click();

    // Remove the link from the DOM
    document.body.removeChild(link);

    // Release the object URL
    window.URL.revokeObjectURL(link.href);
}
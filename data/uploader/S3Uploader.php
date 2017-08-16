<?php
// Include the SDK using the Composer autoloader
require '../../vendor/autoload.php';

use Aws\Credentials\CredentialProvider;
use Aws\S3\S3Client;

class S3Uploader
{
    private $provider;
    private $bucket;

    public function __construct($options)
    {
        $this->provider = CredentialProvider::ini('default', $options['credentials_file_path']);
        $this->bucket = $options['bucket'];        
    }
    
    public function upload($filepath, $keyname, $bucket_relative_path = "")
    {    
        // $provider = CredentialProvider::ini();
        // Cache the results in a memoize function to avoid loading and parsing
        // the ini file on every API operation.

        //$provider = CredentialProvider::memoize($provider);

        $s3 = new Aws\S3\S3Client([
            'version' => 'latest',
            'region'  => 'eu-central-1',
            'credentials' => $this->provider
        ]);


        // download the certifate from http://curl.haxx.se/ca/cacert.pem and define it in php.ini as following: curl.cainfo = "C:\AppServ\cacert.pem"
        // or https://stackoverflow.com/questions/24620393/aws-ssl-security-error-curl-60-ssl-certificate-prob-unable-to-get-local


        // // Use the us-west-2 region and latest version of each client.
        // $sharedConfig = [
        //     'region'  => 'us-west-2',
        //     'version' => 'latest'
        // ];

        // // Create an SDK class used to share configuration across clients.
        // $sdk = new Aws\Sdk($sharedConfig);

        // // Create an Amazon S3 client using the shared configuration data.
        // $client = $sdk->createS3();


        // $upload = $s3->upload($bucket, $_FILES['userfile']['name'], fopen($_FILES['userfile']['tmp_name'], 'rb'), 'public-read');

        // $bucket = "";
        // $filepath = "";
        // $keyname = "";

        // Instantiate the client.
        // $s3 = S3Client::factory();

        try {
            // Upload a file.
            $result = $s3->putObject(array(
                'Bucket'       => $this->bucket,
                'Key'          => (!empty($bucket_relative_path) ? $bucket_relative_path."/" : "").$keyname,
                'SourceFile'   => $filepath,
                //'ContentType'  => 'text/plain',
                //'ContentType'  => 'image/jpeg', //-- autodetect ?
                'ACL'          => 'public-read',
                'StorageClass' => 'REDUCED_REDUNDANCY',
                // 'Metadata'     => array(    
                //     'param1' => 'value 1',
                //     'param2' => 'value 2'
                // )
            ));

            return $result['ObjectURL'];

        } catch (S3Exception $e) {
             echo $e->getMessage() . "\n";
             throw $e;
        }
    }
}
?>